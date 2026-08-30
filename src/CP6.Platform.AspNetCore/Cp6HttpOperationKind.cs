namespace CP6.Platform.AspNetCore;

/// <summary>
/// Declares the replay safety of every request sent by a named HTTP client.
/// </summary>
public enum Cp6HttpOperationKind
{
    IdempotentRead,
    IdempotentWrite,
    NonIdempotent
}
