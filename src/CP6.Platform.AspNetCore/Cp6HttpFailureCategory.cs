namespace CP6.Platform.AspNetCore;

/// <summary>
/// Stable, low-cardinality CP6 outbound HTTP failure categories.
/// </summary>
public enum Cp6HttpFailureCategory
{
    OperationNotAllowed,
    IdempotencyRequired,
    AttemptTimeout,
    TotalTimeout,
    CircuitOpen
}
