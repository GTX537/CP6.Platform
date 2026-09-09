using System.Text;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class FormalPackagePublicationTests
{
    private static readonly DateTimeOffset EvaluationUtc = new(2026, 9, 2, 0, 15, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(FormalVersionMatrix))]
    public void Publication_API_accepts_only_coherent_approved_formal_versions(
        string rootVersion, string packageVersion, string feedVersion, bool expected)
    {
        var fixture = CreateVersionedPublication(rootVersion, packageVersion, feedVersion);
        var error = Record.Exception(() => Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
            Canonical(fixture.Root), fixture.Policy, EvaluationUtc));
        if (expected) Assert.Null(error);
        else Assert.IsType<Cp6ReleaseContractException>(error);
    }

    [Theory]
    [MemberData(nameof(FormalVersionMatrix))]
    public void Publication_Schema_accepts_only_coherent_approved_formal_versions(
        string rootVersion, string packageVersion, string feedVersion, bool expected)
    {
        var fixture = CreateVersionedPublication(rootVersion, packageVersion, feedVersion);
        var result = ReleaseSchemaTestData.Evaluate(fixture.Root);
        Assert.True(result.IsValid == expected, ReleaseSchemaTestData.Errors(result));
    }

    public static TheoryData<string, string, string, bool> FormalVersionMatrix => new()
    {
        { "0.10.0", "0.10.0", "0.10.0", true },
        { "0.10.1", "0.10.1", "0.10.1", true },
        { "0.10.2", "0.10.2", "0.10.2", true },
        { "0.10.0", "0.10.1", "0.10.1", false },
        { "0.10.1", "0.10.0", "0.10.0", false },
        { "0.10.0", "0.10.0", "0.10.1", false },
        { "0.10.1", "0.10.1", "0.10.0", false },
        { "0.10.2", "0.10.1", "0.10.2", false },
        { "0.10.2", "0.10.2", "0.10.1", false },
        { "0.10.1", "0.10.2", "0.10.2", false },
        { "0.10.3", "0.10.3", "0.10.3", false },
        { "0.10.2-preview.1", "0.10.2-preview.1", "0.10.2-preview.1", false },
        { "0.10.2+build.1", "0.10.2+build.1", "0.10.2+build.1", false },
        { "00.10.2", "00.10.2", "00.10.2", false },
        { "0.10.2", "0.10.2", "0.10.2\n", false },
        { "0.10.2\n", "0.10.2\n", "0.10.2\n", false }
    };

    [Fact]
    public void Valid_publication_returns_the_exact_seven_packages_and_subject_hashes()
    {
        var fixture = CreatePublication();

        var validated = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
            Canonical(fixture.Root), fixture.Policy, EvaluationUtc);

        Assert.Equal(7, validated.PackageIds.Count);
        Assert.Equal(validated.PackageIds.Order(StringComparer.Ordinal), validated.PackageIds);
        Assert.Equal(7, validated.SubjectHashes.Count);
    }

    [Fact]
    public void Publication_rejects_package_identity_source_order_and_byte_mutations_with_exact_codes()
    {
        AssertCode("package-version", root => Package(root, 1)["version"] = "0.10.1");
        AssertCode("package-source", root => Package(root, 1)["sourceGitSha"] = new string('c', 40));
        AssertCode("package-version", root => root["version"] = "0.10.0-preview.1");
        AssertCode("package-set", root =>
        {
            var packages = root["packages"]!.AsArray();
            var first = packages[0]!.DeepClone();
            packages[0] = packages[1]!.DeepClone();
            packages[1] = first;
        });
        AssertCode("feed-transformation", root => Package(root, 1)["feedTransformation"] = "Documented");
        AssertCode("package-hash", root => Package(root, 1)["publishedPackageSha256"] = new string('f', 64));
        AssertCode("package-hash", root =>
        {
            var duplicate = Package(root, 0)["authorSignedPackageSha256"]!.GetValue<string>();
            Package(root, 1)["authorSignedPackageSha256"] = duplicate;
            Package(root, 1)["publishedPackageSha256"] = duplicate;
        });
    }

    [Fact]
    public void Publication_rejects_feed_trust_timestamp_gate_and_invocation_mutations_with_exact_codes()
    {
        AssertCode("feed-identity", root => Package(root, 0)["feedIdentity"] = "https://api.nuget.org/v3/index.json");
        AssertCode("trust-signer", root => Package(root, 0)["signerFingerprint"] = new string('f', 64));
        AssertCode("timestamp-policy", root => Package(root, 0)["timestampPolicy"] = "TestOnlyNone");
        AssertCode("timestamp-policy", root => Package(root, 0)["timestampPolicyOid"] = "");
        AssertCode("timestamp-chain", root => Package(root, 0)["timestampCertificateChainSha256"] = new JsonArray());
        AssertCode("verification", root => root["verification"]!["linux"] = "Failure");
        AssertCode("build-invocation", root => root["buildInvocationId"] = $"p10-s04:{new string('c', 40)}:123:1");
    }

    [Fact]
    public void Publication_API_and_Schema_reject_a_feed_identity_for_a_different_package()
    {
        var fixture = CreatePublication();
        var rootVersion = fixture.Root["version"]!.GetValue<string>();
        Package(fixture.Root, 0)["feedIdentity"] =
            $"https://nuget.pkg.github.com/GTX537/index.json#CP6.Platform.Contracts/{rootVersion}";

        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
                Canonical(fixture.Root), fixture.Policy, EvaluationUtc));
        Assert.Equal("feed-identity", exception.Code);
        var result = ReleaseSchemaTestData.Evaluate(fixture.Root);
        Assert.False(result.IsValid, ReleaseSchemaTestData.Errors(result));
    }

    [Fact]
    public void Publication_trust_claims_must_bind_the_supplied_policy()
    {
        AssertCode("trust-policy", root => root["trust"]!["policySha256"] = new string('f', 64));
        AssertCode("trust-signer", root => root["trust"]!["signerFingerprint"] = new string('f', 64));
        AssertCode("trust-signer", root => root["trust"]!["spkiKeyId"] = "sha256:" + new string('f', 64));
    }

    [Fact]
    public void Platform_candidates_accept_byte_preserving_only_for_equal_RFC3161_records()
    {
        var valid = JsonNode.Parse(ReleaseTestData.Fixture("primary", "platform.valid.json"))!.AsObject();
        foreach (var packageNode in valid["packages"]!.AsArray())
        {
            var package = packageNode!.AsObject();
            package["publishedPackageSha256"] = package["authorSignedPackageSha256"]!.GetValue<string>();
            package["feedTransformation"] = "BytePreserving";
            package["timestampPolicy"] = "Rfc3161Required";
        }

        Assert.Equal(7, Cp6ReleaseValidator.ValidatePlatformCandidate(Canonical(valid)).PackageIds.Count);

        var unequal = valid.DeepClone().AsObject();
        Package(unequal, 0)["publishedPackageSha256"] = new string('f', 64);
        Assert.Equal("package-hash", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidatePlatformCandidate(Canonical(unequal))).Code);

        var noTimestamp = valid.DeepClone().AsObject();
        Package(noTimestamp, 0)["timestampPolicy"] = "TestOnlyNone";
        Assert.Equal("timestamp-policy", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidatePlatformCandidate(Canonical(noTimestamp))).Code);
    }

    [Fact]
    public void Build_provenance_accepts_the_S04_invocation_lane()
    {
        var root = JsonNode.Parse(ReleaseTestData.Fixture("supporting", "build-provenance.valid.json"))!.AsObject();
        root["buildInvocationId"] = root["buildInvocationId"]!.GetValue<string>().Replace("p10-s02:", "p10-s04:", StringComparison.Ordinal);

        Assert.Equal(7, Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(Canonical(root)).PackageIds.Count);
    }

    private static void AssertCode(string expectedCode, Action<JsonObject> mutate)
    {
        var fixture = CreatePublication();
        mutate(fixture.Root);
        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
                Canonical(fixture.Root), fixture.Policy, EvaluationUtc));
        Assert.Equal(expectedCode, exception.Code);
    }

    private static PublicationFixture CreatePublication()
    {
        var policyFixture = FormalCertificateTestData.CreatePolicy();
        var policy = Cp6PinnedNuGetTrustPolicy.Parse(policyFixture.CanonicalBytes(), policyFixture.Certificates);
        var root = JsonNode.Parse(ReleaseTestData.Fixture("supporting", "formal-package-publication.valid.json"))!.AsObject();
        var trust = root["trust"]!.AsObject();
        trust["policyVersion"] = policy.PolicyVersion;
        trust["policySha256"] = policy.ValidatedDocument.Sha256;
        trust["signerFingerprint"] = policy.CurrentSigner.CertificateSha256;
        trust["spkiKeyId"] = policy.CurrentSigner.SpkiKeyId;

        var packages = root["packages"]!.AsArray();
        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index]!.AsObject();
            var hash = new string((char)('1' + index), 64);
            package["authorSignedPackageSha256"] = hash;
            package["publishedPackageSha256"] = hash;
            package["signerFingerprint"] = policy.CurrentSigner.CertificateSha256;
        }

        return new(root, policy);
    }

    private static PublicationFixture CreateVersionedPublication(string rootVersion, string packageVersion, string feedVersion)
    {
        var fixture = CreatePublication();
        fixture.Root["version"] = rootVersion;
        foreach (var item in fixture.Root["packages"]!.AsArray())
        {
            var package = item!.AsObject();
            package["version"] = packageVersion;
            package["feedIdentity"] = $"https://nuget.pkg.github.com/GTX537/index.json#{package["packageId"]!.GetValue<string>()}/{feedVersion}";
        }

        return fixture;
    }

    private static JsonObject Package(JsonObject root, int index) =>
        root["packages"]!.AsArray()[index]!.AsObject();

    private static byte[] Canonical(JsonObject root) =>
        Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(root.ToJsonString()));

    private sealed record PublicationFixture(JsonObject Root, Cp6PinnedNuGetTrustPolicy Policy);
}
