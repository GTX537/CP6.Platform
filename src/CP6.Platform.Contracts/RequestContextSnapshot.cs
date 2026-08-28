namespace CP6.Platform.Contracts;

/// <summary>
/// Immutable request identity data produced by a trusted service adapter.
/// </summary>
/// <param name="TenantId">The CP6 organization identifier. It must not be empty.</param>
/// <param name="UserId">The authenticated CP6 user identifier, when the request represents a user.</param>
/// <param name="Subject">The opaque authenticated subject.</param>
/// <param name="Audience">The token audience carried by the trusted adapter.</param>
/// <param name="CorrelationId">The original non-empty correlation identifier.</param>
/// <param name="TokenId">The token identifier, when one is present.</param>
/// <param name="IsPublic">Whether a trusted resolver classified the endpoint as public.</param>
public sealed record RequestContextSnapshot(
    Guid TenantId,
    Guid? UserId,
    string Subject,
    string Audience,
    string CorrelationId,
    string? TokenId,
    bool IsPublic);
