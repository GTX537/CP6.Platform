using System.Net;

namespace CP6.Platform.Testing;

public sealed class Cp6HttpFaultScript
{
    private readonly Cp6HttpFaultOutcome[] outcomes;
    private int nextIndex = -1;

    public Cp6HttpFaultScript(params Cp6HttpFaultOutcome[] outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Any(outcome => outcome is null))
        {
            throw new ArgumentException("Fault outcomes cannot contain null.", nameof(outcomes));
        }

        this.outcomes = [.. outcomes];
    }

    public int AttemptCount => Volatile.Read(ref nextIndex) + 1;

    internal Cp6HttpFaultOutcome Next()
    {
        var index = Interlocked.Increment(ref nextIndex);
        return index < outcomes.Length
            ? outcomes[index]
            : throw new InvalidOperationException("The CP6 HTTP fault script is exhausted.");
    }
}

public sealed record Cp6HttpFaultOutcome
{
    private Cp6HttpFaultOutcome(
        Cp6HttpFaultOutcomeKind kind,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Exception? exception = null,
        TimeSpan delay = default)
    {
        Kind = kind;
        StatusCode = statusCode;
        Exception = exception;
        DelayDuration = delay;
    }

    public static Cp6HttpFaultOutcome Success { get; } = new(Cp6HttpFaultOutcomeKind.Success);

    internal Cp6HttpFaultOutcomeKind Kind { get; }

    internal HttpStatusCode StatusCode { get; }

    internal Exception? Exception { get; }

    internal TimeSpan DelayDuration { get; }

    public static Cp6HttpFaultOutcome Status(HttpStatusCode statusCode)
    {
        var numeric = (int)statusCode;
        if (numeric is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        return new Cp6HttpFaultOutcome(Cp6HttpFaultOutcomeKind.Status, statusCode);
    }

    public static Cp6HttpFaultOutcome Throw(Exception exception) => new(
        Cp6HttpFaultOutcomeKind.Exception,
        exception: exception ?? throw new ArgumentNullException(nameof(exception)));

    public static Cp6HttpFaultOutcome Delay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero || delay > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        return new Cp6HttpFaultOutcome(Cp6HttpFaultOutcomeKind.Delay, delay: delay);
    }
}

internal enum Cp6HttpFaultOutcomeKind
{
    Success = 0,
    Status = 1,
    Exception = 2,
    Delay = 3
}
