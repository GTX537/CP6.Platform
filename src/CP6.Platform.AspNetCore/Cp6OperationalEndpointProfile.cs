using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// Immutable paths and component allowlist for safe CP6 operational endpoints.
/// </summary>
public sealed partial record Cp6OperationalEndpointProfile
{
    public Cp6OperationalEndpointProfile(
        IEnumerable<string> publishedComponentNames,
        string livePath = "/health/live",
        string startupPath = "/health/startup",
        string readyPath = "/health/ready",
        string releasePath = "/health/release")
    {
        ArgumentNullException.ThrowIfNull(publishedComponentNames);
        var componentNames = publishedComponentNames.ToArray();
        foreach (var componentName in componentNames)
        {
            if (string.IsNullOrEmpty(componentName) || !ComponentNamePattern().IsMatch(componentName))
            {
                throw new ArgumentException(
                    "Published component names must be stable canonical names.",
                    nameof(publishedComponentNames));
            }
        }

        ValidatePath(livePath, nameof(livePath));
        ValidatePath(startupPath, nameof(startupPath));
        ValidatePath(readyPath, nameof(readyPath));
        ValidatePath(releasePath, nameof(releasePath));
        if (new[] { livePath, startupPath, readyPath, releasePath }.Distinct(StringComparer.Ordinal).Count() != 4)
        {
            throw new ArgumentException("Operational endpoint paths must be distinct.", nameof(livePath));
        }

        PublishedComponentNames = componentNames.ToFrozenSet(StringComparer.Ordinal);
        LivePath = livePath;
        StartupPath = startupPath;
        ReadyPath = readyPath;
        ReleasePath = releasePath;
    }

    public IReadOnlySet<string> PublishedComponentNames { get; }

    public string LivePath { get; }

    public string StartupPath { get; }

    public string ReadyPath { get; }

    public string ReleasePath { get; }

    internal bool IsEquivalentTo(Cp6OperationalEndpointProfile other) =>
        string.Equals(LivePath, other.LivePath, StringComparison.Ordinal) &&
        string.Equals(StartupPath, other.StartupPath, StringComparison.Ordinal) &&
        string.Equals(ReadyPath, other.ReadyPath, StringComparison.Ordinal) &&
        string.Equals(ReleasePath, other.ReleasePath, StringComparison.Ordinal) &&
        PublishedComponentNames.SetEquals(other.PublishedComponentNames);

    private static void ValidatePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 128 || !PathPattern().IsMatch(path))
        {
            throw new ArgumentException("Operational endpoint path must be a canonical absolute path.", parameterName);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentNamePattern();

    [GeneratedRegex("^/[a-z0-9]+(?:[./-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
