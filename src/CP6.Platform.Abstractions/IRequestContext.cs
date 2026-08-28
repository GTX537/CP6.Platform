namespace CP6.Platform.Abstractions;

/// <summary>
/// Read-only identity and correlation context for one trusted CP6 operation.
/// </summary>
public interface IRequestContext
{
    Guid TenantId { get; }

    Guid? UserId { get; }

    string Subject { get; }

    string Audience { get; }

    string CorrelationId { get; }

    string? TokenId { get; }

    bool IsPublic { get; }
}
