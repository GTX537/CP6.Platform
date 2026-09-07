using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;
using Json.Schema;

namespace CP6.Platform.ReleaseTests;

internal static class ReleaseSchemaTestData
{
    internal static string Errors(EvaluationResults result) => JsonSerializer.Serialize(
        (result.Details ?? []).Where(item => !item.IsValid && item.Errors is not null)
            .Select(item => new { item.InstanceLocation, item.Errors }));

    internal static EvaluationResults Evaluate(JsonNode instance, Func<string, string>? readSchema = null)
    {
        var schemaRoot = Path.Combine(ReleaseTestData.RepositoryRoot, "contracts", "release", "v1");
        readSchema ??= name => File.ReadAllText(Path.Combine(schemaRoot, name));
        var registry = new SchemaRegistry();
        var options = new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        _ = JsonSchema.FromText(readSchema("release-common.v1.schema.json"), options);
        using var assets = JsonDocument.Parse(readSchema("assets.v1.json"));
        var id = instance["$schemaId"]!.GetValue<string>();
        var schemaName = assets.RootElement.GetProperty("schemas").EnumerateArray()
            .Single(asset => asset.GetProperty("id").GetString() == id).GetProperty("path").GetString()!;
        var schema = JsonSchema.FromText(readSchema(schemaName), options);
        using var document = JsonDocument.Parse(instance.ToJsonString());
        return schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    internal static JsonObject Candidate(string stem, string transformation, string timestampPolicy)
    {
        var root = JsonNode.Parse(ReleaseTestData.Fixture("primary", $"{stem}.valid.json"))!.AsObject();
        foreach (var item in root["packages"]!.AsArray())
        {
            var package = item!.AsObject();
            package["version"] = "0.10.1";
            package["feedTransformation"] = transformation;
            package["timestampPolicy"] = timestampPolicy;
            package["publishedPackageSha256"] = package["authorSignedPackageSha256"]!.GetValue<string>();
            package["feedIdentity"] = $"https://nuget.pkg.github.com/GTX537/index.json#{package["packageId"]!.GetValue<string>()}/0.10.1";
        }

        return root;
    }

    internal static Cp6ValidatedReleaseDocument ValidateCandidate(string stem, JsonObject root)
    {
        var canonical = Cp6DeterministicJson.Canonicalize(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        return stem == "platform"
            ? Cp6ReleaseValidator.ValidatePlatformCandidate(canonical)
            : Cp6ReleaseValidator.ValidateSystemCandidate(canonical);
    }
}
