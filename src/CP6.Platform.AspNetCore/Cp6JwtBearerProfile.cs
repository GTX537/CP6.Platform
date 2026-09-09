namespace CP6.Platform.AspNetCore;

/// <summary>
/// Immutable service configuration for the CP6 RS256/JWKS validation boundary.
/// </summary>
public sealed class Cp6JwtBearerProfile
{
    public required string Authority { get; init; }

    public required string Issuer { get; init; }

    public required IReadOnlyCollection<string> Audiences { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;

    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(1);

    internal void Validate()
    {
        if (!TryCreateSafeConfigurationUri(Authority, out var authority))
        {
            throw new ArgumentException("Authority must be an absolute HTTP or HTTPS URI without user information, query, or fragment.", nameof(Authority));
        }

        if (RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Authority must use HTTPS when HTTPS metadata is required.", nameof(Authority));
        }

        if (!TryCreateSafeConfigurationUri(Issuer, out var issuer))
        {
            throw new ArgumentException("Issuer must be an absolute HTTP or HTTPS URI without user information, query, or fragment.", nameof(Issuer));
        }

        if (RequireHttpsMetadata && issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Issuer must use HTTPS when HTTPS metadata is required.", nameof(Issuer));
        }

        if (Audiences is null || Audiences.Count == 0 || Audiences.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty audience is required.", nameof(Audiences));
        }

        if (Audiences.Count != Audiences.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException("Audiences must be unique using ordinal comparison.", nameof(Audiences));
        }

        if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(ClockSkew), "Clock skew must be between zero and five minutes.");
        }
    }

    private static bool TryCreateSafeConfigurationUri(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            HasUserInfoDelimiter(value) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            uri = null!;
            return false;
        }

        return true;
    }

    private static bool HasUserInfoDelimiter(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? value[authorityStart..] : value[authorityStart..authorityEnd];
        return authority.Contains('@', StringComparison.Ordinal);
    }
}
