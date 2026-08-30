namespace CP6.Platform.AspNetCore;

/// <summary>
/// Immutable bounded resilience settings for one named HTTP client and operation kind.
/// </summary>
public sealed record Cp6HttpResilienceProfile
{
    public Cp6HttpResilienceProfile(
        string clientName,
        Cp6HttpOperationKind operationKind,
        int retryAttempts = 2,
        TimeSpan? attemptTimeout = null,
        TimeSpan? totalTimeout = null,
        TimeSpan? circuitSamplingDuration = null,
        int circuitMinimumThroughput = 10,
        TimeSpan? circuitBreakDuration = null)
    {
        if (string.IsNullOrWhiteSpace(clientName) ||
            clientName.Length > 128 ||
            clientName.Trim() != clientName ||
            clientName.Any(char.IsControl))
        {
            throw new ArgumentException("Client name must be a bounded non-empty value.", nameof(clientName));
        }

        if (!Enum.IsDefined(operationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(operationKind), "HTTP operation kind is not supported.");
        }

        if (retryAttempts is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAttempts), "Retry attempts must be between zero and five.");
        }

        var resolvedAttemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(2);
        var resolvedTotalTimeout = totalTimeout ?? TimeSpan.FromSeconds(10);
        var resolvedSamplingDuration = circuitSamplingDuration ?? TimeSpan.FromSeconds(10);
        var resolvedBreakDuration = circuitBreakDuration ?? TimeSpan.FromSeconds(30);
        EnsureBetween(
            resolvedAttemptTimeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(30),
            nameof(attemptTimeout));
        EnsureBetween(
            resolvedTotalTimeout,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(120),
            nameof(totalTimeout));
        EnsureBetween(
            resolvedSamplingDuration,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(120),
            nameof(circuitSamplingDuration));
        EnsureBetween(
            resolvedBreakDuration,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(300),
            nameof(circuitBreakDuration));
        if (circuitMinimumThroughput is < 2 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(circuitMinimumThroughput),
                "Circuit minimum throughput must be between two and one thousand.");
        }

        ClientName = clientName;
        OperationKind = operationKind;
        RetryAttempts = operationKind == Cp6HttpOperationKind.NonIdempotent ? 0 : retryAttempts;
        AttemptTimeout = resolvedAttemptTimeout;
        TotalTimeout = resolvedTotalTimeout;
        CircuitSamplingDuration = resolvedSamplingDuration;
        CircuitMinimumThroughput = circuitMinimumThroughput;
        CircuitBreakDuration = resolvedBreakDuration;
    }

    public string ClientName { get; }

    public Cp6HttpOperationKind OperationKind { get; }

    public int RetryAttempts { get; }

    public TimeSpan AttemptTimeout { get; }

    public TimeSpan TotalTimeout { get; }

    public TimeSpan CircuitSamplingDuration { get; }

    public int CircuitMinimumThroughput { get; }

    public TimeSpan CircuitBreakDuration { get; }

    private static void EnsureBetween(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Duration is outside the approved CP6 bounds.");
        }
    }
}
