namespace CP6.Platform.AspNetCore.HttpV2;

/// <summary>A monotonic budget. Child budgets include the live parent's remainder and cancellation.</summary>
public sealed class Cp6HttpDeadline : IDisposable
{
    private readonly long start;
    private readonly TimeSpan budget;
    private readonly Cp6HttpDeadline? parent;
    private readonly CancellationToken callerCancellation;
    private readonly CancellationTokenSource timer;
    private readonly CancellationTokenSource cancellation;
    public Cp6HttpDeadline(TimeProvider timeProvider, TimeSpan budget, CancellationToken cancellationToken = default)
        : this(timeProvider, budget, cancellationToken, null) { }
    private Cp6HttpDeadline(TimeProvider clock, TimeSpan budget, CancellationToken caller, Cp6HttpDeadline? parent)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (budget < TimeSpan.Zero || budget > TimeSpan.FromSeconds(120)) throw new ArgumentOutOfRangeException(nameof(budget));
        TimeProvider = clock;
        this.parent = parent;
        callerCancellation = caller;
        this.budget = budget;
        start = clock.GetTimestamp();
        ObservedAtUtc = clock.GetUtcNow();
        timer = new CancellationTokenSource(budget, clock);
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, timer.Token, parent?.Token ?? CancellationToken.None);
    }
    public TimeProvider TimeProvider { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public CancellationToken Token => cancellation.Token;
    // Keep original caller sources separate from linked timer tokens throughout the hierarchy.
    internal bool IsCallerCancellationRequested => callerCancellation.IsCancellationRequested ||
        parent?.IsCallerCancellationRequested == true;
    internal void ThrowIfCallerCancellationRequested()
    {
        callerCancellation.ThrowIfCancellationRequested();
        parent?.ThrowIfCallerCancellationRequested();
    }
    public TimeSpan Remaining
    {
        get
        {
            if (Token.IsCancellationRequested) return TimeSpan.Zero;
            var remaining = budget - TimeProvider.GetElapsedTime(start);
            if (parent is not null && parent.Remaining < remaining) remaining = parent.Remaining;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
    public Cp6HttpDeadline CreateChild(TimeSpan requested, CancellationToken cancellationToken = default)
    {
        if (requested < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requested));
        return new(TimeProvider, requested < Remaining ? requested : Remaining, cancellationToken, this);
    }
    public void Dispose() { cancellation.Dispose(); timer.Dispose(); }
}
