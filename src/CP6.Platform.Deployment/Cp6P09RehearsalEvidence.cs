using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace CP6.Platform.Deployment;

public sealed partial class Cp6P09RehearsalEvidence
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] canonicalUtf8;

    private Cp6P09RehearsalEvidence(
        byte[] canonicalUtf8,
        string schemaVersion,
        string profileId,
        string profileSha256,
        string platformGitSha,
        string repositoryVersion,
        string packageVersion,
        string overall,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        IReadOnlyList<Cp6P09EvidenceCheck> checks,
        Cp6P09EvidenceTeardown teardown)
    {
        this.canonicalUtf8 = canonicalUtf8.ToArray();
        SchemaVersion = schemaVersion;
        ProfileId = profileId;
        ProfileSha256 = profileSha256;
        PlatformGitSha = platformGitSha;
        RepositoryVersion = repositoryVersion;
        PackageVersion = packageVersion;
        Overall = overall;
        StartedUtc = startedUtc;
        CompletedUtc = completedUtc;
        Checks = new ReadOnlyCollection<Cp6P09EvidenceCheck>(checks.ToArray());
        Teardown = teardown;
        Sha256 = Cp6P09Json.Sha256Hex(canonicalUtf8);
    }

    public string SchemaVersion { get; }

    public string ProfileId { get; }

    public string ProfileSha256 { get; }

    public string PlatformGitSha { get; }

    public string RepositoryVersion { get; }

    public string PackageVersion { get; }

    public string Overall { get; }

    public DateTimeOffset StartedUtc { get; }

    public DateTimeOffset CompletedUtc { get; }

    public IReadOnlyList<Cp6P09EvidenceCheck> Checks { get; }

    public Cp6P09EvidenceTeardown Teardown { get; }

    public string Sha256 { get; }

    public byte[] ToCanonicalUtf8() => canonicalUtf8.ToArray();

    public static Cp6P09RehearsalEvidence Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] input;
        try
        {
            input = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException exception)
        {
            throw new Cp6P09ContractException("invalid-json", "The evidence is not valid strict UTF-8 JSON.", exception);
        }

        return ParseCore(input);
    }

    public static Cp6P09RehearsalEvidence Parse(ReadOnlySpan<byte> utf8Json) => ParseCore(utf8Json);

    public static Cp6P09RehearsalEvidence Parse(string json, Cp6P09RuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var evidence = Parse(json);
        evidence.ValidateAgainst(profile);
        return evidence;
    }

    public void ValidateAgainst(Cp6P09RuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(ProfileId, profile.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(ProfileSha256, profile.Sha256, StringComparison.Ordinal))
        {
            Fail("profile-mismatch", "Evidence does not identify the supplied canonical runtime profile.");
        }

        var actualCheckIds = Checks.Select(check => check.Id);
        if (!actualCheckIds.SequenceEqual(Cp6P09RuntimeProfileValidator.ExpectedRequiredChecks, StringComparer.Ordinal))
        {
            Fail("required-checks", "Evidence check identifiers differ from the supplied Profile contract.");
        }
    }

    private static Cp6P09RehearsalEvidence ParseCore(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = Cp6P09Json.Canonicalize(utf8Json);
        if (!utf8Json.SequenceEqual(canonical))
        {
            Fail("non-canonical-evidence", "Execution evidence must use canonical compact UTF-8 JSON.");
        }

        using var document = JsonDocument.Parse(canonical);
        ValidateSafety(document.RootElement);
        return CreateValidated(canonical, document.RootElement);
    }
}

public sealed class Cp6P09EvidenceCheck
{
    internal Cp6P09EvidenceCheck(string id, string result, string summary)
    {
        Id = id;
        Result = result;
        Summary = summary;
    }

    public string Id { get; }

    public string Result { get; }

    public string Summary { get; }
}

public sealed class Cp6P09EvidenceTeardown
{
    internal Cp6P09EvidenceTeardown(
        int commandExitCode,
        int containerCount,
        int networkCount,
        int volumeCount,
        int imageCount,
        bool temporaryDirectoryRemoved)
    {
        CommandExitCode = commandExitCode;
        ContainerCount = containerCount;
        NetworkCount = networkCount;
        VolumeCount = volumeCount;
        ImageCount = imageCount;
        TemporaryDirectoryRemoved = temporaryDirectoryRemoved;
    }

    public int CommandExitCode { get; }

    public int ContainerCount { get; }

    public int NetworkCount { get; }

    public int VolumeCount { get; }

    public int ImageCount { get; }

    public bool TemporaryDirectoryRemoved { get; }

    public long ResourceCount => (long)ContainerCount + NetworkCount + VolumeCount + ImageCount;
}
