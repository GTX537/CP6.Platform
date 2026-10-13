using System.Net;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

/// <summary>Library protocol fixtures only. No durable production Owner receipt or exactly-once proof.</summary>
public sealed class OwnerProtocolFixtureTests
{
    [Theory]
    [InlineData(Cp6OriginalResultState.Applied, Cp6HttpOperationState.Applied)]
    [InlineData(Cp6OriginalResultState.Rejected, Cp6HttpOperationState.Rejected)]
    [InlineData(Cp6OriginalResultState.Pending, Cp6HttpOperationState.Pending)]
    public async Task UnknownQueriesOriginalBeforeNewWritePermission_AndReturnsSameResult(Cp6OriginalResultState original, Cp6HttpOperationState expected)
    {
        var binding = Binding(); var authority = new FixtureAuthority { Result = original, AllowReplay = false };
        var transport = new HandlerTests.Scripted((_, _) => throw new HttpRequestException("lost response after possible effect"));
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var request = Request(binding); using var response = await client.SendAsync(request);
        Assert.Equal("original-masked-result", await response.Content.ReadAsStringAsync());
        Assert.Equal(["verify", "query"], authority.Events); Assert.Equal(1, transport.Attempts);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome)); Assert.Equal(expected, outcome.State);
    }
    [Theory]
    [InlineData(Cp6OriginalResultState.Unknown)] [InlineData(Cp6OriginalResultState.NotFound)]
    public async Task UnavailableOrNotFoundQueryPreservesUnknownWithoutAnotherSend(Cp6OriginalResultState result)
    {
        var binding = Binding(); var authority = new FixtureAuthority { Result = result };
        var transport = new HandlerTests.Scripted((_, _) => throw new HttpRequestException());
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var request = Request(binding); var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State); Assert.Equal(1, transport.Attempts);
        Assert.DoesNotContain("authorize", authority.Events);
    }
    [Theory]
    [InlineData(Cp6HttpRetryMode.Disabled, 2)] [InlineData(Cp6HttpRetryMode.OwnerProvenIdempotent, 0)]
    public async Task VerifiedHistoricalQuerySurvivesDisabledOrZeroRetries(Cp6HttpRetryMode mode, int retries)
    {
        var binding = Binding(); var authority = new FixtureAuthority { Result = Cp6OriginalResultState.Applied, AllowReplay = false };
        var transport = new HandlerTests.Scripted((_, _) => throw new HttpRequestException());
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite, mode, retries), transport, binding: binding, authority: authority);
        using var request = Request(binding); using var response = await client.SendAsync(request);
        Assert.Equal(1, transport.Attempts); Assert.Equal(["verify", "query"], authority.Events);
    }
    [Fact]
    public async Task ProvenNotAppliedStillNeedsNewEffectPermission()
    {
        var binding = Binding(); var authority = new FixtureAuthority { AllowReplay = false };
        var transport = new HandlerTests.Scripted((_, _) => throw new HttpRequestException());
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var request = Request(binding); var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.ReplayDenied, error.Outcome.Failure); Assert.Equal(1, transport.Attempts);
        Assert.Equal(["verify", "query", "authorize"], authority.Events);
    }
    [Fact]
    public async Task RetriesKeepExactBodyKeyTenantVersionDigestAndCredentialSnapshot()
    {
        var binding = Binding(); var authority = new FixtureAuthority(); var bodies = new List<byte[]>(); var keys = new List<string>();
        using var request = Request(binding); Assert.True(request.Options.TryGetValue(Cp6HttpOperationIdentity.RequestOption, out var identity));
        var transport = new HandlerTests.Scripted(async (sent, token) =>
        {
            bodies.Add(await sent.Content!.ReadAsByteArrayAsync(token)); keys.Add(sent.Headers.GetValues("Idempotency-Key").Single());
            request.Content = new ByteArrayContent([99]); request.Headers.Remove("Authorization");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer changed-after-start");
            return new HttpResponseMessage(bodies.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var response = await client.SendAsync(request);
        Assert.Equal(2, transport.Attempts); Assert.All(bodies, bytes => Assert.Equal(new byte[] { 1, 2, 3 }, bytes));
        Assert.All(keys, key => Assert.Equal("original-key", key));
        Assert.All(authority.Identities, seen => Assert.Same(identity, seen));
        Assert.All(authority.Snapshots, snapshot => Assert.Equal("Bearer fixture-credential", Assert.Single(snapshot.Headers["Authorization"])));
    }
    [Theory]
    [InlineData("body")] [InlineData("key")] [InlineData("tenant")] [InlineData("epoch")]
    [InlineData("destination")] [InlineData("credential")] [InlineData("method")] [InlineData("version")]
    [InlineData("digest")] [InlineData("client")]
    public async Task ConflictingIdentityAndActualTransportInputsAreRejectedBeforeSend(string conflict)
    {
        var binding = Binding(); var authority = new FixtureAuthority(); using var request = Request(binding);
        switch (conflict)
        {
            case "body": request.Content = new ByteArrayContent([9]); break;
            case "key": request.Headers.Remove("Idempotency-Key"); request.Headers.TryAddWithoutValidation("Idempotency-Key", "other"); break;
            case "tenant": request.Headers.Remove("X-Tenant"); request.Headers.TryAddWithoutValidation("X-Tenant", "tenant-b"); break;
            case "epoch": request.Options.Set(Cp6HttpOperationIdentity.RequestOption, Identity(Binding(epoch: "changed"))); break;
            case "destination": request.RequestUri = new Uri("https://other.example/write"); break;
            case "credential": request.Headers.Remove("Authorization"); request.Headers.TryAddWithoutValidation("Authorization", "Bearer attacker"); break;
            case "method": request.Method = HttpMethod.Delete; break;
            case "version": request.Options.Set(Cp6HttpOperationIdentity.RequestOption, new(binding, "tenant-a", "action", "original-key", "v2", [1, 2, 3])); break;
            case "digest": request.Options.Set(Cp6HttpOperationIdentity.RequestOption, new(binding, "tenant-a", "action", "original-key", "v1", [7])); break;
            case "client": request.Options.Set(Cp6HttpOperationIdentity.RequestOption, Identity(Binding(client: "other"))); break;
        }
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.BindingRejected, error.Outcome.Failure); Assert.Equal(0, transport.Attempts);
    }
    [Fact]
    public async Task HeaderAndSelfDescribedDtoWithoutAuthorityCannotConferProof()
    {
        var binding = Binding(); using var request = Request(binding);
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.BindingRejected, error.Outcome.Failure); Assert.Equal(0, transport.Attempts);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StreamOrOversizeCannotBecomeReplayableByBuffering(bool stream)
    {
        var binding = Binding(); var authority = new FixtureAuthority(); using var request = Request(binding);
        request.Content = stream ? new StreamContent(new MemoryStream([1, 2, 3])) : new ByteArrayContent(new byte[1048577]);
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var response = await client.SendAsync(request); Assert.Equal(1, transport.Attempts); Assert.Empty(authority.Events);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome)); Assert.Equal(Cp6HttpOperationState.Unknown, outcome.State);
    }
    [Fact]
    public async Task UnknownStaysUnknownWhenRetryHitsOpenCircuit()
    {
        var binding = Binding(); var authority = new FixtureAuthority();
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var profile = new Cp6HttpV2Profile("test", Cp6HttpOperationKind.IdempotentWrite, Cp6HttpRetryMode.OwnerProvenIdempotent,
            circuitMinimumThroughput: 2, baseDelayMs: 0, maxDelayMs: 0);
        using var client = HandlerTests.Client(profile, transport, binding: binding, authority: authority);
        using var request = Request(binding); var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.CircuitOpen, error.Outcome.Failure); Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State);
        Assert.Equal(2, transport.Attempts); Assert.Equal(2, authority.Events.Count(e => e == "query"));
    }
    [Fact]
    public void SnapshotCopiesCallerBytesAndFactoryIdentityIsPartOfRegistration()
    {
        var binding = Binding(); var bytes = new byte[] { 1, 2, 3 }; var identity = new Cp6HttpOperationIdentity(binding, "tenant-a", "action", "key", "v1", bytes);
        bytes[0] = 99; var copy = identity.CopyBody(); copy[1] = 99; Assert.Equal(new byte[] { 1, 2, 3 }, identity.CopyBody());
        var services = new ServiceCollection(); var builder = services.AddHttpClient("test");
        var profile = HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite);
        Func<IServiceProvider, ICp6HttpOperationAuthority> factory = _ => new FixtureAuthority();
        builder.AddCp6HttpResilience(profile, binding, factory); var count = services.Count;
        builder.AddCp6HttpResilience(profile, binding, factory); Assert.Equal(count, services.Count);
        Assert.Throws<InvalidOperationException>(() => builder.AddCp6HttpResilience(profile, Binding(epoch: "v2"), factory));
        Assert.Throws<InvalidOperationException>(() => builder.AddCp6HttpResilience(profile, binding, _ => new FixtureAuthority()));
    }
    [Fact]
    public void OneTimeCredentialExchangeCannotEnableReplay()
    {
        var builder = new ServiceCollection().AddHttpClient("test");
        Assert.Throws<InvalidOperationException>(() => builder.AddCp6HttpResilience(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite),
            Binding(oneTime: true), _ => new FixtureAuthority()));
    }
    internal static Cp6HttpOperationBinding Binding(string client = "test", string epoch = "epoch-1", bool oneTime = false, Uri? destination = null) => new(
        "fixture-binding", client, "fixture-owner", HttpMethod.Post, destination ?? new Uri("https://owner.example/write"), "fixture-credential", "config-1", epoch,
        "cp6-http-v2", "fixture-receipt", "fixture-query", "fixture-retention", "fixture-registration", "fixture-runtime",
        "fixture-host", "fixture-endpoint", "tenant-header-matches", "sha256-fixture", "fixture-key-domain", "gateway-v1", oneTime);
    internal static Cp6HttpOperationIdentity Identity(Cp6HttpOperationBinding binding) => new(binding, "tenant-a", "action", "original-key", "v1", [1, 2, 3]);
    internal static HttpRequestMessage Request(Cp6HttpOperationBinding binding)
    {
        var identity = Identity(binding); var request = new HttpRequestMessage(binding.Method, binding.Destination) { Content = new ByteArrayContent(identity.CopyBody()) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", identity.Key);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer fixture-credential"); request.Headers.TryAddWithoutValidation("X-Tenant", "tenant-a");
        request.Options.Set(Cp6HttpOperationIdentity.RequestOption, identity); return request;
    }
    internal sealed class FixtureAuthority : ICp6HttpOperationAuthority
    {
        public List<string> Events { get; } = []; public List<Cp6HttpOperationIdentity> Identities { get; } = [];
        public List<Cp6HttpRequestSnapshot> Snapshots { get; } = [];
        public Cp6OriginalResultState Result { get; init; } = Cp6OriginalResultState.NotAppliedProven;
        public bool AllowReplay { get; init; } = true;
        public Func<Cp6HttpDeadline, CancellationToken, ValueTask<Cp6OriginalResult>>? Query { get; init; }
        public ValueTask<bool> VerifyBindingAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request, Cp6HttpDeadline deadline, CancellationToken token)
        {
            Events.Add("verify"); Identities.Add(operation); Snapshots.Add(request);
            return ValueTask.FromResult(operation.Tenant == "tenant-a" && operation.RequestVersion == "v1" &&
                request.Headers.TryGetValue("Authorization", out var auth) && auth.SequenceEqual(["Bearer fixture-credential"]) &&
                request.Headers.TryGetValue("X-Tenant", out var tenant) && tenant.SequenceEqual([operation.Tenant]));
        }
        public ValueTask<Cp6OriginalResult> QueryOriginalAsync(Cp6HttpOperationIdentity operation, Cp6HttpDeadline deadline, CancellationToken token)
        {
            Events.Add("query"); Identities.Add(operation);
            return Query?.Invoke(deadline, token) ?? ValueTask.FromResult(new Cp6OriginalResult(Result,
                Result is Cp6OriginalResultState.Applied or Cp6OriginalResultState.Rejected or Cp6OriginalResultState.Pending
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("original-masked-result") } : null));
        }
        public ValueTask<bool> AuthorizeReplayAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request, Cp6HttpDeadline deadline, CancellationToken token)
        { Events.Add("authorize"); Identities.Add(operation); Snapshots.Add(request); return ValueTask.FromResult(AllowReplay); }
    }
}
