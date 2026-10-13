using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class DeadlineDecisionTests
{
    [Theory]
    [InlineData(100, 10000, 200, Cp6RetryStop.None)]
    [InlineData(5000, 10000, 5000, Cp6RetryStop.None)]
    [InlineData(60000, 100000, 200, Cp6RetryStop.StopServerWaitExceeded)]
    [InlineData(15000, 10000, 15000, Cp6RetryStop.StopBudgetInsufficient)]
    [InlineData(9000, 10000, 9000, Cp6RetryStop.StopBudgetInsufficient)]
    [InlineData(8000, 10000, 8000, Cp6RetryStop.None)]
    [InlineData(0, 1999, 200, Cp6RetryStop.StopBudgetInsufficient)]
    public void FrozenExamples_UseMaxServerLocalAndReserveFullAttempt(int serverMs, int remainingMs, int waitMs, Cp6RetryStop stop)
    {
        var clock = new TestClock();
        using var deadline = new Cp6HttpDeadline(clock, TimeSpan.FromMilliseconds(remainingMs));
        var server = serverMs > 30000 ? new Cp6RetryAfter(Cp6RetryAfterKind.StopServerWaitExceeded, TimeSpan.Zero)
            : new Cp6RetryAfter(Cp6RetryAfterKind.Valid, TimeSpan.FromMilliseconds(serverMs));
        var decision = Cp6RetryDecision.Create(Profile(), 1, server, clock.GetTimestamp(), deadline, 0.5);
        Assert.Equal(stop, decision.Stop);
        Assert.Equal(TimeSpan.FromMilliseconds(waitMs), decision.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(200), decision.LocalDelay);
    }

    [Fact]
    public void ChildNeverResetsBudget_AndUtcRollbackDoesNotExtendIt()
    {
        var clock = new TestClock();
        using var parent = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(8));
        using var child = parent.CreateChild(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(2), child.Remaining);
        clock.SetUtc(clock.GetUtcNow().AddYears(-1));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), parent.Remaining);
        Assert.Equal(parent.Remaining, child.Remaining);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(child.Token.IsCancellationRequested);
        Assert.Equal(TimeSpan.Zero, child.Remaining);
    }

    [Fact]
    public async Task EarlyWake_KeepsSameSample_AndLateWakeRechecksBudget()
    {
        var clock = new TestClock();
        using var deadline = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10));
        var decision = Cp6RetryDecision.Create(Profile(), 1, default, clock.GetTimestamp(), deadline, 0.5);
        var waits = new List<TimeSpan>();
        Assert.True(await decision.WaitAsync(deadline, TimeSpan.FromSeconds(2), (wait, _) =>
        {
            waits.Add(wait); clock.Advance(TimeSpan.FromMilliseconds(100)); return Task.CompletedTask;
        }));
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100)], waits);
        var late = Cp6RetryDecision.Create(Profile(), 1, default, clock.GetTimestamp(), deadline, 0.5);
        Assert.False(await late.WaitAsync(deadline, TimeSpan.FromSeconds(2), (_, _) =>
        { clock.Advance(TimeSpan.FromSeconds(8)); return Task.CompletedTask; }));
    }

    [Fact]
    public async Task ParseAndQueueTimeCounts_EqualityAllowsFullAttempt_AndUtcChangesDoNotMoveEarliest()
    {
        var clock = new TestClock();
        using var deadline = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10));
        var m0 = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromSeconds(3));
        var decision = Cp6RetryDecision.Create(Profile(), 1, new(Cp6RetryAfterKind.Valid, TimeSpan.FromSeconds(8)), m0, deadline, 0.5);
        Assert.Equal(Cp6RetryStop.None, decision.Stop);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.WaitRemaining(clock));
        clock.SetUtc(clock.GetUtcNow().AddDays(-2));
        Assert.True(await decision.WaitAsync(deadline, TimeSpan.FromSeconds(2), (wait, _) =>
        { clock.Advance(wait); return Task.CompletedTask; }));
        Assert.Equal(TimeSpan.FromSeconds(2), deadline.Remaining);
    }

    [Fact]
    public async Task CancelledWaitNeverAuthorizesAnotherAttempt()
    {
        var clock = new TestClock(); using var cancellation = new CancellationTokenSource();
        using var deadline = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10), cancellation.Token);
        var decision = Cp6RetryDecision.Create(Profile(), 1, default, clock.GetTimestamp(), deadline, 0.5);
        Assert.False(await decision.WaitAsync(deadline, TimeSpan.FromSeconds(2), (_, _) =>
        { cancellation.Cancel(); return Task.CompletedTask; }));
    }

    [Theory]
    [InlineData(0, 160)]
    [InlineData(0.5, 200)]
    [InlineData(1, 240)]
    public void JitterOnlyChangesLocalWait(double sample, int local)
    {
        var clock = new TestClock(); using var deadline = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10));
        var result = Cp6RetryDecision.Create(Profile(), 1, new(Cp6RetryAfterKind.Valid, TimeSpan.FromSeconds(5)), 0, deadline, sample);
        Assert.Equal(TimeSpan.FromMilliseconds(local), result.LocalDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Delay);
        var capped = Cp6RetryDecision.Create(Profile(), 5, default, 0, deadline, 1);
        Assert.Equal(TimeSpan.FromSeconds(2), capped.LocalDelay);
    }
    private static Cp6HttpV2Profile Profile() => new("read", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent, retryAttempts: 5);
}
