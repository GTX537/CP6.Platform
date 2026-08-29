namespace CP6.Platform.AspNetCore;

/// <summary>
/// External identity and proxy metadata that must never be accepted as downstream identity authority.
/// </summary>
public static class Cp6GatewayHeaders
{
    private static readonly string[] UntrustedPrefixes =
    [
        "X-CP6-",
        "X-Organization-",
        "X-Tenant-",
        "X-User-"
    ];

    private static readonly HashSet<string> UntrustedExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Forwarded",
        "Forwarded-Client-Cert",
        "X-CP6",
        "X-Forwarded-Client-Cert",
        "X-Organization",
        "X-Tenant",
        "X-User"
    };

    public static bool IsUntrustedIdentityHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return UntrustedExactNames.Contains(name) ||
            UntrustedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
