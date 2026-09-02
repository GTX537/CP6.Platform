namespace CP6.Platform.Release;

public static class Cp6ReleaseContractIds
{
    public const string Common = "https://schemas.cp6.dev/release/release-common.v1";
    public const string SystemManifest = "https://schemas.cp6.dev/release/system-release-manifest.v1";
    public const string CandidateResult = "https://schemas.cp6.dev/release/candidate-result.v2";
    public const string CandidateLocator = "https://schemas.cp6.dev/release/candidate-locator.v1";
    public const string PlatformCandidate = "https://schemas.cp6.dev/release/platform-release-candidate.v1";
    public const string ReleaseGateResult = "https://schemas.cp6.dev/release/release-gate-result.v1";
    public const string SystemLineageBootstrap = "https://schemas.cp6.dev/release/system-lineage-bootstrap-evidence.v1";
    public const string EvidenceRecord = "https://schemas.cp6.dev/release/evidence-record.v1";
    public const string BuildProvenance = "https://schemas.cp6.dev/release/build-invocation-provenance.v1";
    public const string FormalPackagePublication = "https://schemas.cp6.dev/release/formal-package-publication.v1";
    public const string PinnedNuGetTrustStore = "https://schemas.cp6.dev/release/pinned-nuget-trust-store.v1";
    public const string TestPackageTransport = "https://schemas.cp6.dev/release/test-package-transport.v1";
    public const string PinnedTrustStore = "https://schemas.cp6.dev/release/pinned-trust-store.v1";

    public static IReadOnlyList<string> All { get; } =
    [
        BuildProvenance, CandidateLocator, CandidateResult, EvidenceRecord, FormalPackagePublication,
        PinnedNuGetTrustStore, PinnedTrustStore, PlatformCandidate, ReleaseGateResult,
        SystemLineageBootstrap, SystemManifest, TestPackageTransport
    ];
}
