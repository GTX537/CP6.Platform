using System.Net;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class ReviewRegressionsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ResponseBodyIoFailureQueriesOriginalAndKeepsUnknownWhenUnavailable(bool unavailable)
    {
        var binding = OwnerProtocolFixtureTests.Binding();
        var stream = new ControlledStream(() => throw new IOException("fixture body read failed"));
        var authority = new Authority { Query = (_, _) => new(unavailable ? Cp6OriginalResultState.Unknown : Cp6OriginalResultState.Applied,
            unavailable ? null : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("original") }) };
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        if (unavailable)
        {
            var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
            Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State);
            Assert.Equal(Cp6HttpV2Failure.RecoveryUnavailable, error.Outcome.Failure);
        }
        else
        {
            using var result = await client.SendAsync(request); Assert.Equal("original", await result.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, authority.Queries); Assert.Equal(1, transport.Attempts); Assert.True(stream.Disposed);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out _));
    }

    [Fact]
    public async Task OriginalQueryBodyIoFailureRemainsUnknownAndDisposesBody()
    {
        var binding = OwnerProtocolFixtureTests.Binding(); var stream = new ControlledStream(() => throw new IOException("query fixture"));
        var authority = new Authority { Query = (_, _) => new(Cp6OriginalResultState.Applied,
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }) };
        var transport = UnavailableTransport();
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State); Assert.Equal(Cp6HttpV2Failure.RecoveryUnavailable, error.Outcome.Failure);
        Assert.Equal(1, transport.Attempts); Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompletedQueryBeyondTotalCannotReturnAppliedOrDisabled503(bool applied)
    {
        var clock = new TestClock(); var binding = OwnerProtocolFixtureTests.Binding();
        var stream = new ControlledStream(() => 0);
        var authority = new Authority { Query = (_, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(301));
            return new(applied ? Cp6OriginalResultState.Applied : Cp6OriginalResultState.NotAppliedProven,
                applied ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) } : null);
        } };
        var transport = UnavailableTransport();
        using var client = HandlerTests.Client(Profile(), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure); Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State);
        Assert.Equal(1, transport.Attempts); if (applied) Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData(101, Cp6HttpV2Failure.RecoveryUnavailable)]
    [InlineData(301, Cp6HttpV2Failure.TotalTimeout)]
    public async Task NoncooperativeCompletedQueryBodyCannotEscapeChildOrTotal(int milliseconds, Cp6HttpV2Failure expected)
    {
        var clock = new TestClock(); var binding = OwnerProtocolFixtureTests.Binding();
        var stream = new ControlledStream(() => { clock.Advance(TimeSpan.FromMilliseconds(milliseconds)); return 0; });
        var authority = new Authority { Query = (_, _) => new(Cp6OriginalResultState.Applied,
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }) };
        var transport = UnavailableTransport();
        using var client = HandlerTests.Client(Profile(), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(expected, error.Outcome.Failure); Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State);
        Assert.True(stream.Disposed); Assert.Equal(1, transport.Attempts);
    }

    [Theory]
    [InlineData("verify", false)] [InlineData("verify", true)]
    [InlineData("authorize", false)] [InlineData("authorize", true)]
    [InlineData("query", false)] [InlineData("query", true)]
    public async Task CompletedAuthorityCallbacksCannotOverrideCallerCancellation(string stage, bool answer)
    {
        using var cancellation = new CancellationTokenSource(); var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new Authority
        {
            Verify = () => { if (stage == "verify") { cancellation.Cancel(); return answer; } return true; },
            Authorize = () => { cancellation.Cancel(); return answer; },
            Query = (_, _) =>
            {
                if (stage == "query") cancellation.Cancel();
                return new(answer && stage == "query" ? Cp6OriginalResultState.Applied : Cp6OriginalResultState.NotAppliedProven,
                    answer && stage == "query" ? new HttpResponseMessage(HttpStatusCode.OK) : null);
            }
        };
        var transport = UnavailableTransport();
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), transport, binding: binding, authority: authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(request, cancellation.Token));
        Assert.Equal(stage == "verify" ? 0 : 1, transport.Attempts);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome));
        Assert.Equal(Cp6RetryStop.Cancelled, outcome.RetryStop);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompletedVerificationBeyondTotalDoesNotReportBindingDeniedOrSend(bool answer)
    {
        var clock = new TestClock(); var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new Authority { Verify = () => { clock.Advance(TimeSpan.FromMilliseconds(301)); return answer; } };
        var transport = UnavailableTransport(); using var client = HandlerTests.Client(Profile(), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure); Assert.Equal(Cp6HttpOperationState.NotSent, error.Outcome.State);
        Assert.Equal(0, transport.Attempts);
    }

    private static Cp6HttpV2Profile Profile() => new("test", Cp6HttpOperationKind.IdempotentWrite,
        attemptTimeout: TimeSpan.FromMilliseconds(100), totalTimeout: TimeSpan.FromMilliseconds(300));

    [Theory]
    [InlineData("send")] [InlineData("wait")] [InlineData("query")]
    public async Task InheritedCallerCancellationStaysCallerCancellationAcrossSendWaitAndQuery(string stage)
    {
        var clock = new TestClock(); using var parentCaller = new CancellationTokenSource();
        using var parent = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10), parentCaller.Token);
        using var intermediate = parent.CreateChild(TimeSpan.FromSeconds(9));
        var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new Authority { Query = (_, _) => { parentCaller.Cancel(); return new(Cp6OriginalResultState.NotAppliedProven); } };
        var transport = new HandlerTests.Scripted(async (_, token) =>
        {
            if (stage == "send") { parentCaller.Cancel(); await Task.Delay(Timeout.Infinite, token); }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var profile = new Cp6HttpV2Profile("test", stage == "query" ? Cp6HttpOperationKind.IdempotentWrite : Cp6HttpOperationKind.IdempotentRead,
            Cp6HttpRetryMode.OwnerProvenIdempotent);
        using var client = HandlerTests.Client(profile, transport, clock, binding, authority);
        using var request = stage == "query" ? OwnerProtocolFixtureTests.Request(binding) : new HttpRequestMessage(HttpMethod.Get, "https://owner.example/read");
        request.Options.Set(Cp6HttpV2Handler.ParentDeadlineOption, intermediate);
        var pending = client.SendAsync(request, CancellationToken.None);
        if (stage == "wait") { Assert.False(pending.IsCompleted); parentCaller.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, transport.Attempts);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome));
        Assert.Equal(Cp6RetryStop.Cancelled, outcome.RetryStop); Assert.Equal(Cp6HttpV2Failure.None, outcome.Failure);
    }

    [Fact]
    public async Task InheritedTimerExpiryRemainsTotalTimeoutRatherThanCallerCancellation()
    {
        var clock = new TestClock(); using var parent = new Cp6HttpDeadline(clock, TimeSpan.FromMilliseconds(300));
        var transport = new HandlerTests.Scripted((_, _) =>
        { clock.Advance(TimeSpan.FromMilliseconds(301)); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); });
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentRead), transport, clock);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://owner.example/read");
        request.Options.Set(Cp6HttpV2Handler.ParentDeadlineOption, parent);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure); Assert.Equal(1, transport.Attempts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompletedReplayAuthorizationBeyondTotalCannotDenyOrSendAgain(bool answer)
    {
        var clock = new TestClock(); var binding = OwnerProtocolFixtureTests.Binding();
        var authority = new Authority { Authorize = () => { clock.Advance(TimeSpan.FromMilliseconds(301)); return answer; } };
        var transport = UnavailableTransport();
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentWrite, Cp6HttpRetryMode.OwnerProvenIdempotent,
            attemptTimeout: TimeSpan.FromMilliseconds(100), totalTimeout: TimeSpan.FromMilliseconds(300)), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(request));
        Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure); Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State);
        Assert.Equal(1, transport.Attempts);
    }
    private static HandlerTests.Scripted UnavailableTransport() => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
    private sealed class ControlledStream(Func<int> read) : MemoryStream([1])
    {
        public bool Disposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(read());
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class Authority : ICp6HttpOperationAuthority
    {
        public Func<bool> Verify { get; init; } = () => true;
        public Func<bool> Authorize { get; init; } = () => true;
        public Func<Cp6HttpDeadline, CancellationToken, Cp6OriginalResult> Query { get; init; } = (_, _) => new(Cp6OriginalResultState.NotAppliedProven);
        public int Queries { get; private set; }
        public ValueTask<bool> VerifyBindingAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request, Cp6HttpDeadline deadline, CancellationToken token) => ValueTask.FromResult(Verify());
        public ValueTask<bool> AuthorizeReplayAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request, Cp6HttpDeadline deadline, CancellationToken token) => ValueTask.FromResult(Authorize());
        public ValueTask<Cp6OriginalResult> QueryOriginalAsync(Cp6HttpOperationIdentity operation, Cp6HttpDeadline deadline, CancellationToken token)
        { Queries++; return ValueTask.FromResult(Query(deadline, token)); }
    }
}
