namespace CP6.Platform.Release;

public static class Cp6ReleaseMediaTypes
{
    public const string BuildInvocationProvenance = "application/vnd.cp6.build-invocation-provenance.v1+json";
    public const string CandidateLocator = "application/vnd.cp6.candidate-locator.v1+json";
    public const string CandidateResult = "application/vnd.cp6.candidate-result.v2+json";
    public const string CycloneDx = "application/vnd.cyclonedx+json";
    public const string EvidenceRecord = "application/vnd.cp6.evidence-record.v1+json";
    public const string InToto = "application/vnd.in-toto+json";
    public const string OpenApi = "application/vnd.oai.openapi+json;version=3.1";
    public const string PinnedTrustStore = "application/vnd.cp6.pinned-trust-store.v1+json";
    public const string PlatformReleaseCandidate = "application/vnd.cp6.platform-release-candidate.v1+json";
    public const string ReleaseGateResult = "application/vnd.cp6.release-gate-result.v1+json";
    public const string Sarif = "application/sarif+json";
    public const string Schema = "application/schema+json";
    public const string SigstoreBundle = "application/vnd.dev.sigstore.bundle.v0.3+json";
    public const string Spdx = "application/spdx+json";
    public const string SystemLineageBootstrapEvidence = "application/vnd.cp6.system-lineage-bootstrap-evidence.v1+json";
    public const string SystemReleaseManifest = "application/vnd.cp6.system-release-manifest.v1+json";
    public const string TestPackageTransport = "application/vnd.cp6.test-package-transport.v1+json";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        BuildInvocationProvenance,
        CandidateLocator,
        CandidateResult,
        CycloneDx,
        EvidenceRecord,
        InToto,
        OpenApi,
        PinnedTrustStore,
        PlatformReleaseCandidate,
        ReleaseGateResult,
        Sarif,
        Schema,
        SigstoreBundle,
        Spdx,
        SystemLineageBootstrapEvidence,
        SystemReleaseManifest,
        TestPackageTransport
    }.Order(StringComparer.Ordinal).ToArray();
}
