using System.Text.RegularExpressions;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// A code-owned, validated YARP gateway configuration. Environment-specific addresses remain caller supplied.
/// </summary>
public sealed class Cp6GatewayProfile
{
    public required IReadOnlyCollection<Cp6GatewayRoute> Routes { get; init; }

    public required IReadOnlyCollection<Cp6GatewayCluster> Clusters { get; init; }

    public bool RequireHttpsDestinations { get; init; } = true;
}

public sealed class Cp6GatewayRoute
{
    public required string RouteId { get; init; }

    public required string ClusterId { get; init; }

    public required string MatchPath { get; init; }

    public IReadOnlyCollection<string> Methods { get; init; } = [];

    public string? AuthorizationPolicy { get; init; }

    public int Order { get; init; }

    public required Cp6GatewayRateLimit RateLimit { get; init; }
}

public sealed class Cp6GatewayRateLimit
{
    public required int PermitLimit { get; init; }

    public required TimeSpan Window { get; init; }
}

public sealed class Cp6GatewayCluster
{
    public required string ClusterId { get; init; }

    public required IReadOnlyCollection<Cp6GatewayDestination> Destinations { get; init; }
}

public sealed class Cp6GatewayDestination
{
    public required string DestinationId { get; init; }

    public required Uri Address { get; init; }
}

internal static partial class Cp6GatewayProfileValidator
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"
    };

    public static void Validate(Cp6GatewayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profile.Routes);
        ArgumentNullException.ThrowIfNull(profile.Clusters);

        if (profile.Routes.Count == 0)
        {
            throw new ArgumentException("At least one gateway route is required.", nameof(profile));
        }

        if (profile.Clusters.Count == 0)
        {
            throw new ArgumentException("At least one gateway cluster is required.", nameof(profile));
        }

        var clusters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cluster in profile.Clusters)
        {
            ArgumentNullException.ThrowIfNull(cluster);
            ValidateIdentifier(cluster.ClusterId, "cluster");
            if (!clusters.Add(cluster.ClusterId))
            {
                throw new ArgumentException($"Duplicate gateway cluster id '{cluster.ClusterId}'.", nameof(profile));
            }

            ArgumentNullException.ThrowIfNull(cluster.Destinations);
            if (cluster.Destinations.Count == 0)
            {
                throw new ArgumentException($"Gateway cluster '{cluster.ClusterId}' has no destination.", nameof(profile));
            }

            var destinations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var destination in cluster.Destinations)
            {
                ArgumentNullException.ThrowIfNull(destination);
                ValidateIdentifier(destination.DestinationId, "destination");
                if (!destinations.Add(destination.DestinationId))
                {
                    throw new ArgumentException(
                        $"Duplicate destination id '{destination.DestinationId}' in cluster '{cluster.ClusterId}'.",
                        nameof(profile));
                }

                ValidateDestination(destination.Address, profile.RequireHttpsDestinations);
            }
        }

        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in profile.Routes)
        {
            ArgumentNullException.ThrowIfNull(route);
            ValidateIdentifier(route.RouteId, "route");
            if (!routes.Add(route.RouteId))
            {
                throw new ArgumentException($"Duplicate gateway route id '{route.RouteId}'.", nameof(profile));
            }

            if (!clusters.Contains(route.ClusterId))
            {
                throw new ArgumentException(
                    $"Gateway route '{route.RouteId}' references unknown cluster '{route.ClusterId}'.",
                    nameof(profile));
            }

            if (string.IsNullOrWhiteSpace(route.MatchPath) ||
                !route.MatchPath.StartsWith("/", StringComparison.Ordinal) ||
                route.MatchPath.Contains("\\", StringComparison.Ordinal) ||
                route.MatchPath.Contains("?", StringComparison.Ordinal) ||
                route.MatchPath.Contains("#", StringComparison.Ordinal) ||
                route.MatchPath.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException(
                    $"Gateway route '{route.RouteId}' has an unsafe path pattern.",
                    nameof(profile));
            }

            ArgumentNullException.ThrowIfNull(route.Methods);
            var methods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in route.Methods)
            {
                if (!AllowedMethods.Contains(method) || !methods.Add(method))
                {
                    throw new ArgumentException(
                        $"Gateway route '{route.RouteId}' has an invalid or duplicate HTTP method.",
                        nameof(profile));
                }
            }

            if (route.AuthorizationPolicy is not null && string.IsNullOrWhiteSpace(route.AuthorizationPolicy))
            {
                throw new ArgumentException(
                    $"Gateway route '{route.RouteId}' has an empty authorization policy.",
                    nameof(profile));
            }

            ArgumentNullException.ThrowIfNull(route.RateLimit);
            if (route.RateLimit.PermitLimit is < 1 or > 10_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    $"Gateway route '{route.RouteId}' permit limit must be between 1 and 10000.");
            }

            if (route.RateLimit.Window < TimeSpan.FromSeconds(1) || route.RateLimit.Window > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    $"Gateway route '{route.RouteId}' rate-limit window must be between one second and one hour.");
            }
        }
    }

    private static void ValidateIdentifier(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException($"Gateway {kind} id '{value}' is not a lowercase DNS-style identifier.");
        }
    }

    private static void ValidateDestination(Uri address, bool requireHttps)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri ||
            (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp) ||
            (requireHttps && address.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(address.UserInfo) ||
            !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException("Gateway destinations must be safe absolute HTTP(S) base addresses.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
