using System.Text;
using System.Text.Json.Nodes;
using CP6.Platform.Release;
using CP6.Platform.Release.CapabilitiesV1;
using Fixture = CP6.Platform.ReleaseTests.CapabilitiesV1.CapabilityValidationTests.Fixture;

namespace CP6.Platform.ReleaseTests.CapabilitiesV1;

/// <summary>Synthetic contract tests only; the stub verifier never proves real cryptographic success.</summary>
public sealed class CapabilityTrustTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T00:00:00Z");

    [Theory]
    [InlineData("workflow-run")]
    [InlineData("workflow-file")]
    [InlineData("created-at")]
    public void Changed_publication_metadata_cannot_reuse_an_approved_matrix(string mutation)
    {
        var f = new FormalFixture();
        var approvedMatrixBytes = f.Matrix.Bytes;
        var approval = f.Matrix.Approval;
        if (mutation == "workflow-run")
        {
            f.Publication["workflow"]!["runId"] = 124;
            f.Publication["buildInvocationId"] = $"p10-s04:{Fixture.Source}:124:1";
        }
        if (mutation == "workflow-file") f.Publication["workflow"]!["workflowFileSha"] = new string('c', 40);
        if (mutation == "created-at") f.Publication["createdAtUtc"] = "2026-10-12T00:00:00.000Z";
        // These bytes still satisfy the existing immutable formal grammar and current policy.
        _ = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
            Fixture.Canonical(f.Publication), f.Policy, Now);
        Assert.Equal(approvedMatrixBytes, f.Matrix.Bytes);
        Assert.Equal(approval, f.Matrix.Approval);
        var verifier = new BoundaryVerifier(f, true);
        var error = Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate(verifier));
        Assert.Equal("publication-evidence", error.Code);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public void Formal_publication_must_be_present_in_approved_evidence()
    {
        var f = new FormalFixture();
        var publicationHash = Cp6DeterministicJson.Sha256Hex(Fixture.Canonical(f.Publication));
        f.Matrix.Evidence.Remove(publicationHash);
        f.Matrix.Root["evidenceSha256"] = new JsonArray(f.Matrix.Evidence.Keys.Order(StringComparer.Ordinal)
            .Select(hash => JsonValue.Create(hash)).ToArray());
        var verifier = new BoundaryVerifier(f, true);
        var error = Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate(verifier));
        Assert.Equal("publication-evidence", error.Code);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public void Formal_shape_and_success_claims_do_not_grant_crypto_trust()
    {
        var f = new FormalFixture();
        var result = f.Evaluate();
        Assert.Equal(Cp6CapabilityState.Verified, result.ArtifactBytesVerified);
        Assert.Equal(Cp6CapabilityState.Unknown, result.TrustCurrentlyAccepted);
        Assert.Equal(Cp6CompatibilityDecision.Unknown, result.Compatibility);
    }

    [Fact]
    public void Host_boundary_receives_current_policy_exact_bytes_and_evaluation_time()
    {
        var f = new FormalFixture(); var verifier = new BoundaryVerifier(f, true);
        Assert.Equal(Cp6CapabilityState.Verified, f.Evaluate(verifier).TrustCurrentlyAccepted);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal(Cp6CapabilityState.Unknown, f.Evaluate(new BoundaryVerifier(f, false)).TrustCurrentlyAccepted);
    }

    [Fact]
    public void Task_local_lane_cannot_be_promoted_by_an_external_success()
    {
        var local = new Fixture(); var formal = new FormalFixture(); var verifier = new BoundaryVerifier(formal, true);
        var result = Cp6CapabilityValidator.Evaluate(local.Bytes, local.Approval, local.Packages, local.Evidence,
            Fixture.Canonical(local.Row), Now, Fixture.Canonical(formal.Publication), formal.Policy, verifier);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal("NOT_PUBLISHED", result.PublicationStatus);
        Assert.Equal("NOT_FORMALLY_TRUSTED", result.TrustStatus);
    }

    [Theory]
    [InlineData("Historical")][InlineData("Revoked")][InlineData("unknown")][InlineData("stale-policy")]
    [InlineData("wrong-source")][InlineData("wrong-package")][InlineData("expired")]
    public void Noncurrent_or_mismatched_evidence_never_reaches_crypto_boundary(string mutation)
    {
        var f = new FormalFixture(); var verifier = new BoundaryVerifier(f, true);
        if (mutation is "Historical" or "Revoked")
        {
            var current = FormalCertificateTestData.CreateCertificate();
            var policyFixture = FormalCertificateTestData.CreatePolicyWithSigners(
                new SignerSpec(current, "Current"), new SignerSpec(f.Certificate, mutation));
            f.Policy = Cp6PinnedNuGetTrustPolicy.Parse(policyFixture.CanonicalBytes(), policyFixture.Certificates);
            f.Publication["trust"]!["policySha256"] = f.Policy.ValidatedDocument.Sha256;
        }
        if (mutation == "unknown") f.Publication["trust"]!["signerFingerprint"] = new string('f', 64);
        if (mutation == "stale-policy") f.ApprovedPolicyHash = new string('f', 64);
        if (mutation == "wrong-source") f.Publication["sourceGitSha"] = new string('b', 40);
        if (mutation == "wrong-package")
        {
            f.Publication["packages"]![0]!["publishedPackageSha256"] = new string('f', 64);
            f.Publication["packages"]![0]!["authorSignedPackageSha256"] = new string('f', 64);
        }
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate(verifier, mutation == "expired" ? Now.AddYears(5) : Now));
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public void Future_publication_does_not_establish_current_trust()
    {
        var f = new FormalFixture(); f.Publication["createdAtUtc"] = "2026-10-14T00:00:00.000Z";
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate(new BoundaryVerifier(f, true)));
    }

    [Fact]
    public void Old_formal_package_cannot_claim_later_source_capabilities()
    {
        var f = new FormalFixture(); var local = new Fixture();
        f.Matrix.Root["capabilities"] = local.Root["capabilities"]!.DeepClone();
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Matrix.Bytes));
    }

    private sealed class BoundaryVerifier(FormalFixture expected, bool accepted) : ICp6CapabilitySignatureVerifier
    {
        public int Calls { get; private set; }
        public bool VerifyCurrent(ReadOnlyMemory<byte> publication, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> packages,
            Cp6PinnedNuGetTrustPolicy policy, DateTimeOffset evaluationUtc)
        {
            Calls++;
            Assert.Equal(Fixture.Canonical(expected.Publication), publication.ToArray());
            Assert.Equal(expected.Policy.ValidatedDocument.Sha256, policy.ValidatedDocument.Sha256);
            Assert.Equal(Now, evaluationUtc);
            foreach (var item in expected.Matrix.Packages) Assert.Equal(item.Value.ToArray(), packages[item.Key].ToArray());
            return accepted;
        }
    }

    private sealed class FormalFixture
    {
        public Fixture Matrix { get; } = new(formal: true);
        public TestCertificate Certificate { get; } = FormalCertificateTestData.CreateCertificate();
        public Cp6PinnedNuGetTrustPolicy Policy { get; set; }
        public JsonObject Publication { get; }
        public string? ApprovedPolicyHash { get; set; }
        public FormalFixture()
        {
            var policy = FormalCertificateTestData.CreatePolicy(Certificate);
            Policy = Cp6PinnedNuGetTrustPolicy.Parse(policy.CanonicalBytes(), policy.Certificates);
            Publication = JsonNode.Parse(ReleaseTestData.Fixture("supporting", "formal-package-publication.valid.json"))!.AsObject();
            Publication["version"] = "0.10.2"; Publication["sourceGitSha"] = Fixture.Source;
            Publication["buildInvocationId"] = $"p10-s04:{Fixture.Source}:123:1";
            Publication["workflow"]!["commitSha"] = Fixture.Source; Publication["workflow"]!["runId"] = 123;
            Publication["workflow"]!["runAttempt"] = 1;
            var trust = Publication["trust"]!;
            trust["policyVersion"] = Policy.PolicyVersion; trust["policySha256"] = Policy.ValidatedDocument.Sha256;
            trust["signerFingerprint"] = Certificate.Fingerprint; trust["spkiKeyId"] = Certificate.SpkiKeyId;
            for (var i = 0; i < Fixture.Ids.Length; i++)
            {
                var p = Publication["packages"]![i]!; var m = Matrix.Root["packages"]![i]!;
                p["version"] = "0.10.2"; p["sourceGitSha"] = Fixture.Source;
                p["authorSignedPackageSha256"] = m["sha256"]!.GetValue<string>(); p["publishedPackageSha256"] = m["sha256"]!.GetValue<string>();
                p["feedIdentity"] = m["feedIdentity"]!.GetValue<string>(); p["signerFingerprint"] = Certificate.Fingerprint;
            }
            var publicationBytes = Fixture.Canonical(Publication);
            Matrix.Evidence.Add(Cp6DeterministicJson.Sha256Hex(publicationBytes), publicationBytes);
            Matrix.Root["evidenceSha256"] = new JsonArray(Matrix.Evidence.Keys.Order(StringComparer.Ordinal)
                .Select(hash => JsonValue.Create(hash)).ToArray());
        }
        public Cp6CapabilityEvaluation Evaluate(ICp6CapabilitySignatureVerifier? verifier = null, DateTimeOffset? now = null) =>
            Cp6CapabilityValidator.Evaluate(Matrix.Bytes, Matrix.Approval with { CurrentTrustPolicySha256 = ApprovedPolicyHash ?? Policy.ValidatedDocument.Sha256 },
                Matrix.Packages, Matrix.Evidence, Encoding.UTF8.GetBytes("{}"), now ?? Now, Fixture.Canonical(Publication), Policy, verifier);
    }
}
