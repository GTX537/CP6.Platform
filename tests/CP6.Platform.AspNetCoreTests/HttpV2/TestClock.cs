namespace CP6.Platform.AspNetCoreTests.HttpV2;

internal sealed class TestClock : TimeProvider
{
    private long ticks;
    private DateTimeOffset utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<ClockTimer> timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public override DateTimeOffset GetUtcNow() => utc;
    public void SetUtc(DateTimeOffset value) => utc = value;
    public void Advance(TimeSpan amount)
    {
        ticks += amount.Ticks; utc += amount;
        foreach (var timer in timers.ToArray()) timer.Fire(ticks);
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ClockTimer(this, callback, state);
        timers.Add(timer); timer.Change(dueTime, period); return timer;
    }
    private sealed class ClockTimer(TestClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        private TimeSpan period;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan nextPeriod)
        {
            if (disposed) return false;
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks;
            period = nextPeriod; return true;
        }
        public void Fire(long now)
        {
            if (disposed || due > now) return;
            due = period <= TimeSpan.Zero ? long.MaxValue : now + period.Ticks;
            callback(state);
        }
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
