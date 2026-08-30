using System.Text.RegularExpressions;
using CP6.Platform.Contracts;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// Immutable service identity and deployment dimensions used to compose CP6 telemetry.
/// </summary>
public sealed partial record Cp6ObservabilityProfile(
    string ServiceName,
    string ServiceVersion,
    string EnvironmentName,
    string Region,
    Cp6ReleaseIdentity ReleaseIdentity)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(ReleaseIdentity);
        ReleaseIdentity.Validate();

        ValidateCanonicalName(ServiceName, 64, nameof(ServiceName));
        ValidateCanonicalName(EnvironmentName, 32, nameof(EnvironmentName));
        ValidateCanonicalName(Region, 32, nameof(Region));

        if (!string.Equals(ServiceName, ReleaseIdentity.Service, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The observability service name must match the release identity service.",
                nameof(ServiceName));
        }

        if (!string.Equals(ServiceVersion, ReleaseIdentity.Version, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The observability service version must match the release identity version.",
                nameof(ServiceVersion));
        }
    }

    private static void ValidateCanonicalName(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            !CanonicalNamePattern().IsMatch(value))
        {
            throw new ArgumentException(
                "Value must be a bounded canonical lowercase name.",
                parameterName);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalNamePattern();
}
