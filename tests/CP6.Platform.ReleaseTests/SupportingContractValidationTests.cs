using System.Text.Json;
using CP6.Platform.Release;
using Json.Schema;

namespace CP6.Platform.ReleaseTests;

public sealed class SupportingContractValidationTests
{
    private static readonly DateTimeOffset EvaluationUtc = DateTimeOffset.Parse(
        "2026-09-01T00:00:00.000Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [MemberData(nameof(StructuralMutations))]
    public void Supporting_entry_points_reject_structural_mutations(string stem, string invalidFixture, string code)
    {
        _ = Validate(stem, $"{stem}.valid.json");
        var exception = Assert.Throws<Cp6ReleaseContractException>(() => Validate(stem, invalidFixture));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Object_authority_hash_addressing_and_evidence_binding_fail_closed()
    {
        var valid = Cp6SupportingContractValidator.ValidateEvidenceRecord(ReleaseTestData.Fixture("supporting", "evidence.valid.json"));
        Assert.Equal("https://schemas.cp6.dev/release/evidence-record.v1", valid.SchemaId);
        Assert.Equal("storage-authority", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.ValidateEvidenceRecord(ReleaseTestData.Fixture("supporting", "evidence-authority.invalid.json"))).Code);
        Assert.Equal("evidence-binding", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.ValidateEvidenceRecord(ReleaseTestData.Fixture("supporting", "evidence-unbound.invalid.json"))).Code);
    }

    [Fact]
    public void Build_provenance_requires_one_invocation_and_seven_mapped_outputs()
    {
        var valid = Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(ReleaseTestData.Fixture("supporting", "build-provenance.valid.json"));
        Assert.Equal(7, valid.PackageIds.Count);
        Assert.Equal("build-invocation", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(ReleaseTestData.Fixture("supporting", "build-provenance-mixed-invocation.invalid.json"))).Code);
    }

    [Fact]
    public void Bootstrap_and_successor_lineage_require_opposite_evidence_presence()
    {
        var system = ReleaseTestData.Fixture("primary", "system.valid.json");
        var bootstrap = ReleaseTestData.Fixture("supporting", "lineage-bootstrap.valid.json");
        Cp6SupportingContractValidator.RequireSystemLineage(system, bootstrap);
        Assert.Equal("bootstrap-required", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.RequireSystemLineage(system, [])).Code);

        var successor = ReleaseTestData.Fixture("supporting", "system-successor.valid.json");
        Cp6SupportingContractValidator.RequireSystemLineage(successor, []);
        Assert.Equal("bootstrap-forbidden", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.RequireSystemLineage(successor, bootstrap)).Code);
    }

    [Fact]
    public void Transport_requires_test_only_unexpired_exact_run_binding()
    {
        var valid = Cp6SupportingContractValidator.ValidateTestPackageTransport(
            ReleaseTestData.Fixture("supporting", "transport.valid.json"),
            EvaluationUtc);
        Assert.Equal("https://schemas.cp6.dev/release/test-package-transport.v1", valid.SchemaId);
        Assert.Equal("transport-expired", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.ValidateTestPackageTransport(
                ReleaseTestData.Fixture("supporting", "transport-expired.invalid.json"),
                EvaluationUtc)).Code);
    }

    [Fact]
    public void Locator_time_must_equal_referenced_subject_creation_time()
    {
        Cp6SupportingContractValidator.RequireCandidateLocatorSubjectBinding(
            ReleaseTestData.Fixture("primary", "candidate-locator-platform.valid.json"),
            ReleaseTestData.Fixture("primary", "platform.valid.json"));
        Assert.Equal("locator-created-at", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.RequireCandidateLocatorSubjectBinding(
                ReleaseTestData.Fixture("supporting", "candidate-locator-time.invalid.json"),
                ReleaseTestData.Fixture("primary", "platform.valid.json"))).Code);
    }

    [Fact]
    public void Required_public_evidence_must_cover_every_accepted_subject()
    {
        var evidence = Cp6SupportingContractValidator.ValidateEvidenceRecord(ReleaseTestData.Fixture("supporting", "evidence.valid.json"));
        Cp6SupportingContractValidator.RequireRequiredPublicEvidence([evidence], evidence.SubjectHashes);
        Assert.Equal("required-public-evidence", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6SupportingContractValidator.RequireRequiredPublicEvidence([], evidence.SubjectHashes)).Code);
    }

    [Theory]
    [InlineData("pinned-nuget-trust-store.v1.schema.json", "pinned-nuget-trust.valid.json")]
    [InlineData("formal-package-publication.v1.schema.json", "formal-package-publication.valid.json")]
    [InlineData("formal-package-publication.v1.schema.json", "formal-package-publication-changed-published-bytes.invalid.json")]
    public void Formal_supporting_fixtures_match_their_structural_contract(string schemaName, string fixtureName)
    {
        var result = EvaluateSchema(schemaName, fixtureName);
        Assert.True(result.IsValid, JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("pinned-nuget-trust-store.v1.schema.json", "pinned-nuget-trust-public-ca.invalid.json")]
    [InlineData("pinned-nuget-trust-store.v1.schema.json", "pinned-nuget-trust-wrong-package-set.invalid.json")]
    [InlineData("pinned-nuget-trust-store.v1.schema.json", "pinned-nuget-trust-two-current.invalid.json")]
    [InlineData("pinned-nuget-trust-store.v1.schema.json", "pinned-nuget-trust-bad-fingerprint.invalid.json")]
    [InlineData("pinned-nuget-trust-store.v1.schema.json", "pinned-nuget-trust-bad-status-fields.invalid.json")]
    [InlineData("formal-package-publication.v1.schema.json", "formal-package-publication-mixed-version.invalid.json")]
    [InlineData("formal-package-publication.v1.schema.json", "formal-package-publication-test-timestamp-policy.invalid.json")]
    [InlineData("formal-package-publication.v1.schema.json", "formal-package-publication-missing-package.invalid.json")]
    public void Formal_supporting_schemas_reject_structural_mutations(string schemaName, string fixtureName)
    {
        Assert.False(EvaluateSchema(schemaName, fixtureName).IsValid);
    }

    [Fact]
    public void Formal_supporting_fixture_bytes_are_canonical_except_the_explicit_negative_case()
    {
        var fixtureRoot = Path.Combine(ReleaseTestData.RepositoryRoot, "contracts", "release", "v1", "fixtures", "supporting");
        var fixtures = Directory.GetFiles(fixtureRoot, "pinned-nuget-trust*.json")
            .Concat(Directory.GetFiles(fixtureRoot, "formal-package-publication*.json"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var fixture in fixtures)
        {
            var bytes = File.ReadAllBytes(fixture);
            var canonical = Cp6DeterministicJson.Canonicalize(bytes);
            if (fixture.EndsWith("pinned-nuget-trust-noncanonical.invalid.json", StringComparison.Ordinal))
            {
                Assert.False(bytes.AsSpan().SequenceEqual(canonical));
            }
            else
            {
                Assert.True(bytes.AsSpan().SequenceEqual(canonical), $"Fixture must be canonical: {fixture}");
            }
        }
    }

    public static TheoryData<string, string, string> StructuralMutations => new()
    {
        { "release-gate", "release-gate-missing.invalid.json", "missing-property" },
        { "release-gate", "release-gate-unknown.invalid.json", "unknown-property" },
        { "release-gate", "release-gate-wrong-kind.invalid.json", "property-kind" },
        { "lineage-bootstrap", "lineage-bootstrap-missing.invalid.json", "missing-property" },
        { "lineage-bootstrap", "lineage-bootstrap-unknown.invalid.json", "unknown-property" },
        { "lineage-bootstrap", "lineage-bootstrap-wrong-kind.invalid.json", "property-kind" },
        { "evidence", "evidence-missing.invalid.json", "missing-property" },
        { "evidence", "evidence-unknown.invalid.json", "unknown-property" },
        { "evidence", "evidence-wrong-kind.invalid.json", "property-kind" },
        { "build-provenance", "build-provenance-missing.invalid.json", "missing-property" },
        { "build-provenance", "build-provenance-unknown.invalid.json", "unknown-property" },
        { "build-provenance", "build-provenance-wrong-kind.invalid.json", "property-kind" },
        { "transport", "transport-missing.invalid.json", "missing-property" },
        { "transport", "transport-unknown.invalid.json", "unknown-property" },
        { "transport", "transport-wrong-kind.invalid.json", "property-kind" },
        { "trust", "trust-missing.invalid.json", "missing-property" },
        { "trust", "trust-unknown.invalid.json", "unknown-property" },
        { "trust", "trust-wrong-kind.invalid.json", "property-kind" }
    };

    private static Cp6ValidatedReleaseDocument Validate(string stem, string fixture) => stem switch
    {
        "release-gate" => Cp6SupportingContractValidator.ValidateReleaseGateResult(ReleaseTestData.Fixture("supporting", fixture)),
        "lineage-bootstrap" => Cp6SupportingContractValidator.ValidateSystemLineageBootstrapEvidence(ReleaseTestData.Fixture("supporting", fixture)),
        "evidence" => Cp6SupportingContractValidator.ValidateEvidenceRecord(ReleaseTestData.Fixture("supporting", fixture)),
        "build-provenance" => Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(ReleaseTestData.Fixture("supporting", fixture)),
        "transport" => Cp6SupportingContractValidator.ValidateTestPackageTransport(ReleaseTestData.Fixture("supporting", fixture), EvaluationUtc),
        "trust" => Cp6PinnedTrustPolicy.Parse(ReleaseTestData.Fixture("supporting", fixture)).ValidatedDocument,
        _ => throw new ArgumentOutOfRangeException(nameof(stem))
    };

    private static EvaluationResults EvaluateSchema(string schemaName, string fixtureName)
    {
        var schemaRoot = Path.Combine(ReleaseTestData.RepositoryRoot, "contracts", "release", "v1");
        var registry = new SchemaRegistry();
        var buildOptions = new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        _ = JsonSchema.FromText(File.ReadAllText(Path.Combine(schemaRoot, "release-common.v1.schema.json")), buildOptions);
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(schemaRoot, schemaName)), buildOptions);
        using var fixture = JsonDocument.Parse(ReleaseTestData.Fixture("supporting", fixtureName));
        return schema.Evaluate(fixture.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    }
}
