using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Messaging;

namespace CP6.Platform.UnitTests;

public sealed class SchemaCompatibilityTests
{
    private static readonly string PublishedSchema = File.ReadAllText(FindSchemaPath());

    [Fact]
    public void AddOptionalProperty_RemainsBackwardCompatible()
    {
        var candidate = ParseCandidate();
        var dataProperties = candidate["properties"]!["data"]!["properties"]!.AsObject();
        dataProperties["displayCode"] = new JsonObject
        {
            ["type"] = "string",
            ["maxLength"] = 32
        };

        var result = Cp6SchemaCompatibility.Compare(PublishedSchema, candidate.ToJsonString());

        Assert.True(result.IsBackwardCompatible);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AddRequiredProperty_IsBreaking()
    {
        var candidate = ParseCandidate();
        candidate["properties"]!["data"]!["required"]!.AsArray().Add("displayCode");

        var result = Cp6SchemaCompatibility.Compare(PublishedSchema, candidate.ToJsonString());

        Assert.False(result.IsBackwardCompatible);
        Assert.Contains(result.Issues, issue => issue.Code == "REQUIRED_SET_CHANGED" && issue.SchemaLocation == "/properties/data");
    }

    [Fact]
    public void ChangePublishedType_IsBreaking()
    {
        var candidate = ParseCandidate();
        candidate["properties"]!["aggregateversion"]!["type"] = "string";

        var result = Cp6SchemaCompatibility.Compare(PublishedSchema, candidate.ToJsonString());

        Assert.False(result.IsBackwardCompatible);
        Assert.Contains(result.Issues, issue => issue.Code == "CONSTRAINT_CHANGED" && issue.SchemaLocation.EndsWith("/type", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectUnknownProperties_IsBreaking()
    {
        var candidate = ParseCandidate();
        candidate["additionalProperties"] = false;

        var result = Cp6SchemaCompatibility.Compare(PublishedSchema, candidate.ToJsonString());

        Assert.False(result.IsBackwardCompatible);
        Assert.Contains(result.Issues, issue => issue.Code == "UNKNOWN_PROPERTIES_REJECTED");
    }

    private static JsonObject ParseCandidate() => JsonNode.Parse(PublishedSchema)!.AsObject();

    private static string FindSchemaPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "contracts", "events", "platform", "contract-example-changed", "v1", "schema.json");
            if (File.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate the published example schema.");
    }
}
