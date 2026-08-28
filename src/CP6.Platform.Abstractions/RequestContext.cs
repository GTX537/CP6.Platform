using CP6.Platform.Contracts;

namespace CP6.Platform.Abstractions;

/// <summary>
/// Validated immutable implementation of <see cref="IRequestContext"/>.
/// </summary>
public sealed class RequestContext : IRequestContext
{
    public RequestContext(RequestContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId must be a non-empty CP6 organization identifier.", nameof(snapshot));
        }

        TenantId = snapshot.TenantId;
        UserId = snapshot.UserId;
        Subject = RequireValue(snapshot.Subject, nameof(snapshot.Subject));
        Audience = RequireValue(snapshot.Audience, nameof(snapshot.Audience));
        CorrelationId = RequireValue(snapshot.CorrelationId, nameof(snapshot.CorrelationId));
        TokenId = NormalizeOptional(snapshot.TokenId);
        IsPublic = snapshot.IsPublic;
    }

    public Guid TenantId { get; }

    public Guid? UserId { get; }

    public string Subject { get; }

    public string Audience { get; }

    public string CorrelationId { get; }

    public string? TokenId { get; }

    public bool IsPublic { get; }

    private static string RequireValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value must not be empty or whitespace.", parameterName);
        }

        return value;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
