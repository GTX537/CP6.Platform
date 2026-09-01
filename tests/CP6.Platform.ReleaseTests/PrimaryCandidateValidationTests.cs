using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class PrimaryCandidateValidationTests
{
    [Fact]
    public void Platform_fixture_has_exact_non_deployable_seven_package_identity()
    {
        var document = Cp6ReleaseValidator.ValidatePlatformCandidate(ReleaseTestData.Fixture("primary", "platform.valid.json"));
        Assert.Equal("PlatformReference", document.CandidateKind);
        Assert.False(document.Deployable);
        Assert.Equal(7, document.PackageIds.Count);
        Assert.Equal(document.PackageIds.Order(StringComparer.Ordinal), document.PackageIds);
    }

    [Fact]
    public void System_validator_requires_four_exact_repositories_and_deployable_true()
    {
        var document = Cp6ReleaseValidator.ValidateSystemCandidate(ReleaseTestData.Fixture("primary", "system.valid.json"));
        Assert.True(document.Deployable);
        Assert.Equal(["CP6", "CP6.CRM", "CP6.Platform", "CP6.Portal"], document.RepositoryNames);
        Assert.Equal("repository-set", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidateSystemCandidate(ReleaseTestData.Fixture("primary", "system-missing-portal.invalid.json"))).Code);
    }

    [Fact]
    public void Candidate_lanes_cannot_be_substituted()
    {
        Assert.Equal("candidate-kind", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidateSystemCandidate(ReleaseTestData.Fixture("primary", "platform-as-system.invalid.json"))).Code);
        Assert.Equal("candidate-kind", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidatePlatformCandidate(ReleaseTestData.Fixture("primary", "system.valid.json"))).Code);
    }

    [Fact]
    public void Platform_packages_require_one_version_and_one_source_sha()
    {
        Assert.Equal("package-version", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidatePlatformCandidate(ReleaseTestData.Fixture("primary", "platform-mixed-package-version.invalid.json"))).Code);
    }

    [Fact]
    public void Both_locator_subject_lanes_are_valid_and_distinct()
    {
        var system = Cp6ReleaseValidator.ValidateCandidateLocator(ReleaseTestData.Fixture("primary", "candidate-locator-system.valid.json"));
        var platform = Cp6ReleaseValidator.ValidateCandidateLocator(ReleaseTestData.Fixture("primary", "candidate-locator-platform.valid.json"));
        Assert.Equal("SystemCandidateResult", system.SubjectKind);
        Assert.Equal("PlatformReleaseCandidate", platform.SubjectKind);
    }

    [Theory]
    [MemberData(nameof(StructuralMutations))]
    public void Primary_entry_points_reject_structural_mutations(string entryPoint, string validFixture, string invalidFixture, string code)
    {
        _ = Validate(entryPoint, ReleaseTestData.Fixture("primary", validFixture));
        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Validate(entryPoint, ReleaseTestData.Fixture("primary/mutations", invalidFixture)));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Media_types_are_exact_unique_and_used_by_all_primary_object_references()
    {
        Assert.Equal(17, Cp6ReleaseMediaTypes.All.Count);
        Assert.Equal(Cp6ReleaseMediaTypes.All.Count, Cp6ReleaseMediaTypes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Cp6ReleaseMediaTypes.All.Order(StringComparer.Ordinal), Cp6ReleaseMediaTypes.All);

        var primaryRoot = Path.Combine(ReleaseTestData.RepositoryRoot, "contracts", "release", "v1", "fixtures", "primary");
        foreach (var path in Directory.GetFiles(primaryRoot, "*.valid.json", SearchOption.TopDirectoryOnly))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            AssertObjectReferenceMediaTypes(document.RootElement);
        }
    }

    public static TheoryData<string, string, string, string> StructuralMutations => new()
    {
        { "system", "system.valid.json", "system-missing.invalid.json", "missing-property" },
        { "system", "system.valid.json", "system-unknown.invalid.json", "unknown-property" },
        { "system", "system.valid.json", "system-wrong-kind.invalid.json", "property-kind" },
        { "candidate-result", "candidate-result.valid.json", "candidate-result-missing.invalid.json", "missing-property" },
        { "candidate-result", "candidate-result.valid.json", "candidate-result-unknown.invalid.json", "unknown-property" },
        { "candidate-result", "candidate-result.valid.json", "candidate-result-wrong-kind.invalid.json", "property-kind" },
        { "candidate-locator", "candidate-locator-platform.valid.json", "candidate-locator-missing.invalid.json", "missing-property" },
        { "candidate-locator", "candidate-locator-platform.valid.json", "candidate-locator-unknown.invalid.json", "unknown-property" },
        { "candidate-locator", "candidate-locator-platform.valid.json", "candidate-locator-wrong-kind.invalid.json", "property-kind" },
        { "platform", "platform.valid.json", "platform-missing.invalid.json", "missing-property" },
        { "platform", "platform.valid.json", "platform-unknown.invalid.json", "unknown-property" },
        { "platform", "platform.valid.json", "platform-wrong-kind.invalid.json", "property-kind" }
    };

    private static Cp6ValidatedReleaseDocument Validate(string entryPoint, byte[] value) => entryPoint switch
    {
        "system" => Cp6ReleaseValidator.ValidateSystemCandidate(value),
        "candidate-result" => Cp6ReleaseValidator.ValidateCandidateResult(value),
        "candidate-locator" => Cp6ReleaseValidator.ValidateCandidateLocator(value),
        "platform" => Cp6ReleaseValidator.ValidatePlatformCandidate(value),
        _ => throw new ArgumentOutOfRangeException(nameof(entryPoint))
    };

    private static void AssertObjectReferenceMediaTypes(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("storageAuthority", out _) && value.TryGetProperty("mediaType", out var mediaType))
            {
                Assert.Contains(mediaType.GetString(), Cp6ReleaseMediaTypes.All);
            }

            foreach (var property in value.EnumerateObject()) AssertObjectReferenceMediaTypes(property.Value);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) AssertObjectReferenceMediaTypes(item);
        }
    }
}
