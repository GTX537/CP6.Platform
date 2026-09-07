using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class ReleaseSchemaParityTests
{
    [Theory]
    [MemberData(nameof(PackagePolicyMatrix))]
    public void Candidate_API_and_Schema_agree_on_feed_and_timestamp_policy(
        string stem, string transformation, string timestampPolicy, bool expected)
    {
        var root = ReleaseSchemaTestData.Candidate(stem, transformation, timestampPolicy);
        if (expected)
        {
            Assert.NotEmpty(ReleaseSchemaTestData.ValidateCandidate(stem, root).PackageIds);
        }
        else
        {
            Assert.Throws<Cp6ReleaseContractException>(() => ReleaseSchemaTestData.ValidateCandidate(stem, root));
        }

        var result = ReleaseSchemaTestData.Evaluate(root);
        Assert.True(result.IsValid == expected, ReleaseSchemaTestData.Errors(result));
    }

    [Theory]
    [MemberData(nameof(PositiveFixtures))]
    public void Every_positive_primary_and_supporting_fixture_matches_its_Schema(string group, string name)
    {
        var root = JsonNode.Parse(ReleaseTestData.Fixture(group, name))!;
        var result = ReleaseSchemaTestData.Evaluate(root);
        Assert.True(result.IsValid, ReleaseSchemaTestData.Errors(result));
    }

    [Fact]
    public void Common_object_media_type_allowlist_matches_the_API_exactly()
    {
        var path = Path.Combine(ReleaseTestData.RepositoryRoot, "contracts", "release", "v1", "release-common.v1.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var schemaTypes = document.RootElement.GetProperty("$defs").GetProperty("objectReference")
            .GetProperty("properties").GetProperty("mediaType").GetProperty("enum").EnumerateArray()
            .Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Cp6ReleaseMediaTypes.All, schemaTypes);
    }

    [Theory]
    [MemberData(nameof(ContractMediaTypes))]
    public void Candidate_API_can_reference_each_owned_contract(string mediaType)
    {
        var root = ReleaseSchemaTestData.Candidate("platform", "BytePreserving", "Rfc3161Required");
        root["evidence"]![0]!["mediaType"] = mediaType;
        Assert.NotEmpty(ReleaseSchemaTestData.ValidateCandidate("platform", root).PackageIds);
    }

    [Theory]
    [MemberData(nameof(ContractMediaTypes))]
    public void Candidate_Schema_can_reference_each_owned_contract(string mediaType)
    {
        var root = ReleaseSchemaTestData.Candidate("platform", "BytePreserving", "Rfc3161Required");
        root["evidence"]![0]!["mediaType"] = mediaType;
        var result = ReleaseSchemaTestData.Evaluate(root);
        Assert.True(result.IsValid, ReleaseSchemaTestData.Errors(result));
    }

    public static TheoryData<string> ContractMediaTypes => new(
        Cp6ReleaseContractIds.All.Select(id => $"application/vnd.cp6.{id[(id.LastIndexOf('/') + 1)..]}+json"));

    public static TheoryData<string, string, string, bool> PackagePolicyMatrix
    {
        get
        {
            var data = new TheoryData<string, string, string, bool>();
            foreach (var stem in new[] { "platform", "system" })
                foreach (var transformation in new[] { "None", "Documented", "BytePreserving", "Bytepreserving", "Unknown" })
                    foreach (var timestamp in new[] { "TestOnlyNone", "Rfc3161Required" })
                    {
                        var expected = transformation is "None" or "Documented" ||
                            transformation == "BytePreserving" && timestamp == "Rfc3161Required";
                        data.Add(stem, transformation, timestamp, expected);
                    }

            return data;
        }
    }

    public static TheoryData<string, string> PositiveFixtures
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var group in new[] { "primary", "supporting" })
            {
                var path = Path.Combine(ReleaseTestData.RepositoryRoot, "contracts", "release", "v1", "fixtures", group);
                foreach (var fixture in Directory.GetFiles(path, "*.valid.json").Order(StringComparer.Ordinal))
                    data.Add(group, Path.GetFileName(fixture));
            }

            return data;
        }
    }
}
