namespace CP6.Platform.AspNetCore.HttpV2;

public enum Cp6RetryStop { None, StopServerWaitExceeded, StopBudgetInsufficient, RetryLimit, Cancelled }

/// <summary>One immutable jitter sample anchored to the response's first observable timestamp.</summary>
public sealed record Cp6RetryDecision(Cp6RetryStop Stop, Cp6RetryAfterKind HeaderKind,
    long ObservedTimestamp, TimeSpan Delay, TimeSpan LocalDelay)
{
    public static Cp6RetryDecision Create(Cp6HttpV2Profile profile, int retryNumber, Cp6RetryAfter server,
        long observedTimestamp, Cp6HttpDeadline deadline, double sample)
    {
        if (!double.IsFinite(sample) || sample is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(sample));
        var basis = (decimal)Math.Min(profile.MaxDelayMs, profile.BaseDelayMs * Math.Pow(2, Math.Clamp(retryNumber - 1, 0, 5)));
        var ratio = (decimal)profile.JitterRatio;
        var local = TimeSpan.FromMilliseconds((double)decimal.Ceiling(Math.Clamp(
            basis * (1 - ratio + 2 * ratio * (decimal)sample), 0, profile.MaxDelayMs)));
        var delay = server.Delay > local ? server.Delay : local;
        var result = new Cp6RetryDecision(Cp6RetryStop.None, server.Kind, observedTimestamp, delay, local);
        var stop = server.Kind == Cp6RetryAfterKind.StopServerWaitExceeded ? Cp6RetryStop.StopServerWaitExceeded
            : retryNumber < 1 || retryNumber > profile.RetryAttempts ? Cp6RetryStop.RetryLimit
            : deadline.Token.IsCancellationRequested ? Cp6RetryStop.Cancelled
            : result.WaitRemaining(deadline.TimeProvider) + profile.AttemptTimeout > deadline.Remaining ? Cp6RetryStop.StopBudgetInsufficient
            : Cp6RetryStop.None;
        return result with { Stop = stop };
    }
    public TimeSpan WaitRemaining(TimeProvider clock)
    {
        var remaining = Delay - clock.GetElapsedTime(ObservedTimestamp);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
    public async Task<bool> WaitAsync(Cp6HttpDeadline deadline, TimeSpan attemptTimeout,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (Stop != Cp6RetryStop.None) return false;
        while (!deadline.Token.IsCancellationRequested)
        {
            var wait = WaitRemaining(deadline.TimeProvider);
            if (wait + attemptTimeout > deadline.Remaining) return false;
            if (wait == TimeSpan.Zero) return true;
            if (delay is null) await Task.Delay(wait, deadline.TimeProvider, deadline.Token).ConfigureAwait(false);
            else await delay(wait, deadline.Token).ConfigureAwait(false);
        }
        return false;
    }
}
