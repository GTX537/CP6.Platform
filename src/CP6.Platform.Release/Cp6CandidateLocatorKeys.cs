using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

public sealed record Cp6CandidateLocatorKeys(string LocatorKey, string BundleKey)
{
    private static readonly Regex Tag = new(
        "^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$",
        RegexOptions.CultureInvariant);

    public static Cp6CandidateLocatorKeys ForPlatformTag(string releaseTag)
    {
        if (!Tag.IsMatch(releaseTag) || releaseTag.Contains("..", StringComparison.Ordinal))
            throw new Cp6ReleaseContractException("release-tag", "Release tag is not canonical or path-safe.");
        var prefix = $"candidates/platform/{releaseTag}";
        return new($"{prefix}/candidate-locator.v1.json", $"{prefix}/candidate-locator.v1.sigstore.json");
    }
}
