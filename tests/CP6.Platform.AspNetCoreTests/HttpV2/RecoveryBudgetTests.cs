using System.Net;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class RecoveryBudgetTests
{
    [Fact]
    public async Task FailedSendAnchorsLocalWaitWhenFailureIsObserved()
    {
        var clock = new TestClock(); var attempts = 0;
        var transport = new HandlerTests.Scripted((_, _) =>
        {
            if (++attempts == 1) { clock.Advance(TimeSpan.FromMilliseconds(500)); throw new HttpRequestException(); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent), transport, clock);
        var pending = client.GetAsync("https://owner.example/read");
        Assert.Equal(1, attempts); clock.Advance(TimeSpan.FromMilliseconds(199)); Assert.Equal(1, attempts);
        clock.Advance(TimeSpan.FromMilliseconds(1)); using var response = await pending.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(2, attempts);
    }
    [Fact]
    public async Task ParentWithDifferentClockUsesParentDomainForAnchors()
    {
        var handlerClock = new TestClock(); handlerClock.Advance(TimeSpan.FromDays(1));
        var parentClock = new TestClock(); using var parent = new Cp6HttpDeadline(parentClock, TimeSpan.FromSeconds(10)); var attempts = 0;
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(++attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent), transport, handlerClock);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://owner.example/read"); request.Options.Set(Cp6HttpV2Handler.ParentDeadlineOption, parent);
        var pending = client.SendAsync(request); parentClock.Advance(TimeSpan.FromMilliseconds(200));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(2, attempts);
    }
    [Fact]
    public async Task QueryUsesRemainingParentBudget_AndCannotCreateAnotherSend()
    {
        var clock = new TestClock(); var binding = OwnerProtocolFixtureTests.Binding(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authority = new OwnerProtocolFixtureTests.FixtureAuthority
        {
            Query = async (deadline, token) =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(150), deadline.Remaining); entered.SetResult();
                await Task.Delay(Timeout.Infinite, token); return new(Cp6OriginalResultState.Applied);
            }
        };
        var transport = new HandlerTests.Scripted((_, _) => { clock.Advance(TimeSpan.FromMilliseconds(150)); throw new HttpRequestException(); });
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentWrite, Cp6HttpRetryMode.OwnerProvenIdempotent,
            attemptTimeout: TimeSpan.FromMilliseconds(200), totalTimeout: TimeSpan.FromMilliseconds(300)), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding); var pending = client.SendAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); clock.Advance(TimeSpan.FromMilliseconds(150));
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => pending);
        Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State); Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure);
        Assert.Equal(1, transport.Attempts); Assert.Equal(1, authority.Events.Count(e => e == "query"));
    }
    [Fact]
    public async Task OriginalResultBodyCannotEscapeQueryDeadline()
    {
        var clock = new TestClock(); var binding = OwnerProtocolFixtureTests.Binding(); var body = new HandlerTests.BlockingStream();
        var authority = new OwnerProtocolFixtureTests.FixtureAuthority
        {
            Query = (_, _) => ValueTask.FromResult(new Cp6OriginalResult(Cp6OriginalResultState.Applied,
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }))
        };
        var transport = new HandlerTests.Scripted((_, _) => throw new HttpRequestException());
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentWrite, Cp6HttpRetryMode.OwnerProvenIdempotent,
            attemptTimeout: TimeSpan.FromMilliseconds(100), totalTimeout: TimeSpan.FromMilliseconds(500)), transport, clock, binding, authority);
        using var request = OwnerProtocolFixtureTests.Request(binding); var pending = client.SendAsync(request);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); clock.Advance(TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => pending);
        Assert.Equal(Cp6HttpOperationState.Unknown, error.Outcome.State); Assert.Equal(Cp6HttpV2Failure.RecoveryUnavailable, error.Outcome.Failure);
        Assert.Equal(1, transport.Attempts);
    }
    [Fact]
    public async Task CallerCancellationPreservesSentWriteUnknownWithoutPretendingToCancelBusiness()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new HandlerTests.Scripted(async (_, token) => { cancellation.Cancel(); await Task.Delay(1, token); return new(HttpStatusCode.OK); });
        using var client = HandlerTests.Client(HandlerTests.Profile(Cp6HttpOperationKind.NonIdempotent), transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://owner.example/write");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(request, cancellation.Token));
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome));
        Assert.Equal(Cp6HttpOperationState.Unknown, outcome.State); Assert.Equal(Cp6RetryStop.Cancelled, outcome.RetryStop); Assert.Equal(1, transport.Attempts);
    }
    [Fact]
    public async Task HalfOpenUsesSameCircuitAndClosesOnSuccessfulProbe()
    {
        var clock = new TestClock(); var count = 0;
        var transport = new HandlerTests.Scripted((_, _) => Task.FromResult(new HttpResponseMessage(++count <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        using var client = HandlerTests.Client(new("test", Cp6HttpOperationKind.IdempotentRead, circuitMinimumThroughput: 2,
            circuitBreakDuration: TimeSpan.FromSeconds(1)), transport, clock);
        using var first = await client.GetAsync("https://owner.example/read"); using var second = await client.GetAsync("https://owner.example/read");
        await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync("https://owner.example/read"));
        clock.Advance(TimeSpan.FromSeconds(1)); using var probe = await client.GetAsync("https://owner.example/read");
        using var after = await client.GetAsync("https://owner.example/read");
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode); Assert.Equal(HttpStatusCode.OK, after.StatusCode); Assert.Equal(4, count);
    }
}
