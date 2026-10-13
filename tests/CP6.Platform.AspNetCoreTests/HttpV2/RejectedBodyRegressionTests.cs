using System.Net;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class RejectedBodyRegressionTests
{
    public static IEnumerable<object[]> BodyFailures()
    {
        foreach (var status in new[] { 200, 401, 403, 409, 422, 503 })
        foreach (var write in new[] { false, true })
        foreach (var timeout in new[] { false, true })
            yield return [status, write, timeout];
    }

    [Theory]
    [MemberData(nameof(BodyFailures))]
    public async Task ObservedRejectionVetoesReplayAfterBodyFailure(int status, bool write, bool timeout)
    {
        var clock = new TestClock();
        var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new OwnerProtocolFixtureTests.FixtureAuthority();
        var bodies = new List<FailingBody>();
        var transport = new HandlerTests.Scripted((_, _) =>
        {
            var body = new FailingBody(clock, timeout);
            bodies.Add(body);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StreamContent(body) });
        });
        var profile = new Cp6HttpV2Profile("test", write ? Cp6HttpOperationKind.IdempotentWrite : Cp6HttpOperationKind.IdempotentRead,
            Cp6HttpRetryMode.OwnerProvenIdempotent, attemptTimeout: TimeSpan.FromMilliseconds(100),
            totalTimeout: TimeSpan.FromSeconds(1), baseDelayMs: 0, maxDelayMs: 0);
        using var client = HandlerTests.Client(profile, transport, clock, binding, authority);
        using var request = write ? OwnerProtocolFixtureTests.Request(binding) : new HttpRequestMessage(HttpMethod.Get, "https://owner.example/read");
        request.Options.TryGetValue(Cp6HttpOperationIdentity.RequestOption, out var original);

        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));

        var expectedAttempts = status is 200 or 503 ? 3 : 1;
        Assert.Equal(expectedAttempts, transport.Attempts);
        Assert.Equal(expectedAttempts, error.Outcome.Attempts);
        Assert.Equal(timeout ? Cp6HttpV2Failure.AttemptTimeout : Cp6HttpV2Failure.Transport, error.Outcome.Failure);
        Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State);
        Assert.Same(original, error.Outcome.OriginalIdentity);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var recorded));
        Assert.Equal(error.Outcome, recorded);
        Assert.All(bodies, body => Assert.True(body.Disposed));
        Assert.Equal(write ? expectedAttempts : 0, authority.Events.Count(x => x == "query"));
        Assert.Equal(write ? expectedAttempts - 1 : 0, authority.Events.Count(x => x == "authorize"));
    }

    private sealed class FailingBody(TestClock clock, bool timeout) : MemoryStream([1])
    {
        public bool Disposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!timeout) throw new IOException("fixture incomplete response body");
            clock.Advance(TimeSpan.FromMilliseconds(101));
            return ValueTask.FromResult(0);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
