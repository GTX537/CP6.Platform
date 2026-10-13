namespace CP6.Platform.AspNetCore.HttpV2;

public enum Cp6HttpRetryMode { Disabled, OwnerProvenIdempotent }

/// <summary>Explicit opt-in settings. Reads are intrinsically replayable; writes need a registered Owner authority.</summary>
public sealed record Cp6HttpV2Profile
{
    public Cp6HttpV2Profile(string clientName, Cp6HttpOperationKind operationKind,
        Cp6HttpRetryMode retryMode = Cp6HttpRetryMode.Disabled, int retryAttempts = 2,
        TimeSpan? attemptTimeout = null, TimeSpan? totalTimeout = null,
        TimeSpan? circuitSamplingDuration = null, int circuitMinimumThroughput = 10,
        TimeSpan? circuitBreakDuration = null, int baseDelayMs = 200, int maxDelayMs = 2000,
        double jitterRatio = 0.2, int requestReplayLimitBytes = 1048576,
        int responseLimitBytes = 1048576, string policyVersion = "cp6-http-v2")
    {
        var legacy = new Cp6HttpResilienceProfile(clientName, operationKind, retryAttempts, attemptTimeout,
            totalTimeout, circuitSamplingDuration, circuitMinimumThroughput, circuitBreakDuration);
        if (!Enum.IsDefined(retryMode)) throw new ArgumentOutOfRangeException(nameof(retryMode));
        if (legacy.AttemptTimeout > legacy.TotalTimeout) throw new ArgumentException("Attempt timeout exceeds total timeout.");
        if (baseDelayMs is < 0 or > 30000 || maxDelayMs < baseDelayMs || maxDelayMs > 30000)
            throw new ArgumentOutOfRangeException(nameof(baseDelayMs));
        if (!double.IsFinite(jitterRatio) || jitterRatio is < 0 or > 0.5) throw new ArgumentOutOfRangeException(nameof(jitterRatio));
        if (requestReplayLimitBytes is < 1 or > 1048576 || responseLimitBytes is < 1 or > 16777216)
            throw new ArgumentOutOfRangeException(nameof(requestReplayLimitBytes));
        RequireReference(policyVersion);
        ClientName = clientName;
        OperationKind = operationKind;
        RetryMode = operationKind == Cp6HttpOperationKind.NonIdempotent ? Cp6HttpRetryMode.Disabled : retryMode;
        RetryAttempts = legacy.RetryAttempts;
        AttemptTimeout = legacy.AttemptTimeout;
        TotalTimeout = legacy.TotalTimeout;
        CircuitSamplingDuration = legacy.CircuitSamplingDuration;
        CircuitMinimumThroughput = legacy.CircuitMinimumThroughput;
        CircuitBreakDuration = legacy.CircuitBreakDuration;
        BaseDelayMs = baseDelayMs;
        MaxDelayMs = maxDelayMs;
        JitterRatio = jitterRatio;
        RequestReplayLimitBytes = requestReplayLimitBytes;
        ResponseLimitBytes = responseLimitBytes;
        PolicyVersion = policyVersion;
    }
    public string ClientName { get; }
    public Cp6HttpOperationKind OperationKind { get; }
    public Cp6HttpRetryMode RetryMode { get; }
    public int RetryAttempts { get; }
    public TimeSpan AttemptTimeout { get; }
    public TimeSpan TotalTimeout { get; }
    public TimeSpan CircuitSamplingDuration { get; }
    public int CircuitMinimumThroughput { get; }
    public TimeSpan CircuitBreakDuration { get; }
    public int BaseDelayMs { get; }
    public int MaxDelayMs { get; }
    public double JitterRatio { get; }
    public int RequestReplayLimitBytes { get; }
    public int ResponseLimitBytes { get; }
    public string PolicyVersion { get; }
    internal static void RequireReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Trim() != value || value.Any(char.IsControl))
            throw new ArgumentException("A bounded immutable reference is required.");
    }
}
