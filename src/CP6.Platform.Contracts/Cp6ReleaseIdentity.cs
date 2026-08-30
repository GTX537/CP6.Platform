using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace CP6.Platform.Contracts;

/// <summary>
/// Identifies whether a running service is an immutable release candidate or an explicit local/test build.
/// </summary>
public enum Cp6ReleaseMode
{
    NonCandidate,
    Candidate
}

/// <summary>
/// Immutable release identity exposed by CP6 operational and evidence contracts.
/// </summary>
public sealed partial record Cp6ReleaseIdentity
{
    public Cp6ReleaseIdentity(
        string service,
        string version,
        string gitSha,
        string artifactDigest,
        string contractBundleDigest,
        Cp6ReleaseMode mode)
    {
        Service = service;
        Version = version;
        GitSha = gitSha;
        ArtifactDigest = artifactDigest;
        ContractBundleDigest = contractBundleDigest;
        Mode = mode;
    }

    [JsonConstructor]
    public Cp6ReleaseIdentity(
        string service,
        string version,
        string gitSha,
        string artifactDigest,
        string contractBundleDigest,
        bool candidate)
        : this(
            service,
            version,
            gitSha,
            artifactDigest,
            contractBundleDigest,
            candidate ? Cp6ReleaseMode.Candidate : Cp6ReleaseMode.NonCandidate)
    {
    }

    [JsonPropertyName("service")]
    public string Service { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; }

    [JsonPropertyName("gitSha")]
    public string GitSha { get; init; }

    [JsonPropertyName("artifactDigest")]
    public string ArtifactDigest { get; init; }

    [JsonPropertyName("contractBundleDigest")]
    public string ContractBundleDigest { get; init; }

    [JsonIgnore]
    public Cp6ReleaseMode Mode { get; init; }

    [JsonPropertyName("candidate")]
    public bool Candidate => Mode == Cp6ReleaseMode.Candidate;

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode), "Release mode is not supported.");
        }

        if (string.IsNullOrWhiteSpace(Service) ||
            Service.Length > 64 ||
            !ServicePattern().IsMatch(Service))
        {
            throw new ArgumentException("Service must be a canonical lowercase service name.", nameof(Service));
        }

        if (string.IsNullOrWhiteSpace(Version) ||
            Version.Length > 128 ||
            Version.Trim() != Version ||
            Version.Any(char.IsControl))
        {
            throw new ArgumentException("Version must be a bounded non-empty value.", nameof(Version));
        }

        if (Candidate && !SemanticVersionPattern().IsMatch(Version))
        {
            throw new ArgumentException("Candidate version must be canonical SemVer without build metadata.", nameof(Version));
        }

        ValidateOptionalGitSha(GitSha, Candidate);
        ValidateOptionalDigest(ArtifactDigest, Candidate, nameof(ArtifactDigest));
        ValidateOptionalDigest(ContractBundleDigest, Candidate, nameof(ContractBundleDigest));
    }

    private static void ValidateOptionalGitSha(string value, bool required)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (required)
            {
                throw new ArgumentException("Candidate Git SHA is required.", nameof(GitSha));
            }

            return;
        }

        if (!GitShaPattern().IsMatch(value))
        {
            throw new ArgumentException("Git SHA must contain exactly 40 lowercase hexadecimal characters.", nameof(GitSha));
        }
    }

    private static void ValidateOptionalDigest(string value, bool required, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (required)
            {
                throw new ArgumentException("Candidate SHA-256 digest is required.", parameterName);
            }

            return;
        }

        if (!Sha256DigestPattern().IsMatch(value))
        {
            throw new ArgumentException("Digest must be canonical lowercase SHA-256.", parameterName);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ServicePattern();

    [GeneratedRegex(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitShaPattern();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256DigestPattern();
}
