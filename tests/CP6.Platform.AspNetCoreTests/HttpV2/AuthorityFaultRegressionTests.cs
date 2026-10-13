using System.Net;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using Polly.Timeout;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class AuthorityFaultRegressionTests
{
    public static IEnumerable<object[]> Faults()
    {
        foreach (var stage in new[] { "verify", "replay" })
        foreach (var asynchronous in new[] { false, true })
        foreach (var fault in new[] { "transport", "io", "timeout", "other" })
            yield return [stage, asynchronous, fault];
    }

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task AuthorityFaultRetainsSafeOriginalOutcome(string stage, bool asynchronous, string fault)
    {
        var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new FaultingAuthority(stage, asynchronous, () => ExceptionFor(fault));
        var responses = new List<HttpResponseMessage>();
        var transport = Transport(responses);
        using var client = HandlerTests.Client(Profile(), transport, binding: binding, authority: authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        Assert.True(request.Options.TryGetValue(Cp6HttpOperationIdentity.RequestOption, out var identity));

        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));

        Assert.Equal(fault == "timeout" ? Cp6HttpV2Failure.UnknownTimeoutSource : Cp6HttpV2Failure.RecoveryUnavailable, error.Outcome.Failure);
        Assert.Equal(stage == "verify" ? Cp6HttpOperationState.NotSent : Cp6HttpOperationState.Unknown, error.Outcome.State);
        Assert.Equal(stage == "verify" ? 0 : 1, error.Outcome.Attempts);
        Assert.Equal(error.Outcome.Attempts, transport.Attempts);
        Assert.Same(identity, error.Outcome.OriginalIdentity);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var recorded));
        Assert.Equal(error.Outcome, recorded);
        Assert.DoesNotContain("private", error.Message);
        Assert.Equal(stage == "verify" ? 0 : 1, authority.Queries);
        Assert.Equal(stage == "verify" ? 0 : 1, authority.Replays);
        Assert.All(responses, response => Assert.Throws<ObjectDisposedException>(() => response.Content.ReadAsStream()));
    }

    [Theory]
    [InlineData("verify", "caller")] [InlineData("replay", "caller")]
    [InlineData("verify", "parent-caller")] [InlineData("replay", "parent-caller")]
    [InlineData("verify", "total")] [InlineData("replay", "total")]
    public async Task CancellationAndDeadlineTakePrecedenceOverConcurrentAuthorityFault(string stage, string signal)
    {
        var clock = new TestClock();
        using var caller = new CancellationTokenSource();
        using var parentCaller = new CancellationTokenSource();
        using var parent = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(2), parentCaller.Token);
        var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new FaultingAuthority(stage, true, () =>
        {
            if (signal == "caller") caller.Cancel();
            else if (signal == "parent-caller") parentCaller.Cancel();
            else clock.Advance(TimeSpan.FromMilliseconds(501));
            return new HttpRequestException("private authority dependency failure");
        });
        var transport = Transport([]);
        using var client = HandlerTests.Client(Profile(), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        request.Options.Set(Cp6HttpV2Handler.ParentDeadlineOption, parent);

        if (signal == "total")
        {
            var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request, caller.Token));
            Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure);
        }
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(request, caller.Token));

        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome));
        Assert.Equal(stage == "verify" ? Cp6HttpOperationState.NotSent : Cp6HttpOperationState.Unknown, outcome.State);
        Assert.Equal(stage == "verify" ? 0 : 1, transport.Attempts);
        Assert.Equal(transport.Attempts, outcome.Attempts);
        if (signal != "total") Assert.Equal(Cp6RetryStop.Cancelled, outcome.RetryStop);
    }

    [Theory]
    [InlineData("verify")] [InlineData("replay")]
    public async Task UnrelatedAuthorityCancellationRemainsUnknownSource(string stage)
    {
        var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new FaultingAuthority(stage, false, () => new OperationCanceledException(new CancellationToken(true)));
        var transport = Transport([]);
        using var client = HandlerTests.Client(Profile(), transport, binding: binding, authority: authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.UnknownTimeoutSource, error.Outcome.Failure);
        Assert.Equal(stage == "verify" ? 0 : 1, transport.Attempts);
    }

    private static Cp6HttpV2Profile Profile() => new("test", Cp6HttpOperationKind.IdempotentWrite,
        Cp6HttpRetryMode.OwnerProvenIdempotent, attemptTimeout: TimeSpan.FromMilliseconds(100),
        totalTimeout: TimeSpan.FromMilliseconds(500), baseDelayMs: 0, maxDelayMs: 0);

    private static HandlerTests.Scripted Transport(List<HttpResponseMessage> responses) => new((_, _) =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("unavailable") };
        responses.Add(response);
        return Task.FromResult(response);
    });

    private static Exception ExceptionFor(string fault) => fault switch
    {
        "transport" => new HttpRequestException("private authority dependency failure"),
        "io" => new IOException("private authority dependency failure"),
        "timeout" => new TimeoutRejectedException("private timeout source"),
        _ => new InvalidOperationException("private authority adapter failure")
    };

    private sealed class FaultingAuthority(string stage, bool asynchronous, Func<Exception> fault) : ICp6HttpOperationAuthority
    {
        public int Queries { get; private set; }
        public int Replays { get; private set; }
        private ValueTask<bool> Fail()
        {
            var exception = fault();
            if (asynchronous) return new(Task.FromException<bool>(exception));
            throw exception;
        }
        public ValueTask<bool> VerifyBindingAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request,
            Cp6HttpDeadline deadline, CancellationToken token) => stage == "verify" ? Fail() : ValueTask.FromResult(true);
        public ValueTask<Cp6OriginalResult> QueryOriginalAsync(Cp6HttpOperationIdentity operation, Cp6HttpDeadline deadline, CancellationToken token)
        {
            Queries++;
            return ValueTask.FromResult(new Cp6OriginalResult(Cp6OriginalResultState.NotAppliedProven));
        }
        public ValueTask<bool> AuthorizeReplayAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request,
            Cp6HttpDeadline deadline, CancellationToken token)
        {
            Replays++;
            return Fail();
        }
    }
}
