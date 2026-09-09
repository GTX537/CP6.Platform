using System.Text.Json;
using CP6.Platform.Release;
using Json.Schema;

namespace CP6.Platform.ReleaseTests;

public sealed class ReleaseSchemaAssetTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Asset_manifest_lists_all_contracts_once_in_ordinal_order()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "contracts", "release", "v1", "assets.v1.json")));
        var common = document.RootElement.GetProperty("commonSchema");
        Assert.Equal(Cp6ReleaseContractIds.Common, common.GetProperty("id").GetString());
        Assert.Equal("release-common.v1.schema.json", common.GetProperty("path").GetString());
        Assert.Equal("application/schema+json", common.GetProperty("mediaType").GetString());
        var assets = document.RootElement.GetProperty("schemas").EnumerateArray().ToArray();
        Assert.Equal(Cp6ReleaseContractIds.All, assets.Select(asset => asset.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(assets.Length, assets.Select(asset => asset.GetProperty("path").GetString()!).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Formal_contract_assets_are_closed_and_registered_once()
    {
        var path = Path.Combine(
            ReleaseTestData.RepositoryRoot,
            "contracts", "release", "v1", "assets.v1.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var ids = document.RootElement.GetProperty("schemas")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();
        Assert.Single(ids, id => id == Cp6ReleaseContractIds.PinnedNuGetTrustStore);
        Assert.Single(ids, id => id == Cp6ReleaseContractIds.FormalPackagePublication);
    }

    [Fact]
    public void Formal_publication_schema_registers_exactly_the_three_immutable_versions_at_every_layer()
    {
        var path = Path.Combine(Root, "contracts", "release", "v1", "formal-package-publication.v1.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var expected = new[] { "0.10.0", "0.10.1", "0.10.2" };

        Assert.Equal(expected, root.GetProperty("properties").GetProperty("version").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(expected, root.GetProperty("$defs").GetProperty("package").GetProperty("properties")
            .GetProperty("version").GetProperty("enum").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(expected, root.GetProperty("allOf").EnumerateArray()
            .Select(item => item.GetProperty("if").GetProperty("properties").GetProperty("version").GetProperty("const").GetString())
            .ToArray());
    }

    [Fact]
    public void Every_schema_is_draft_2020_12_closed_and_buildable()
    {
        var schemaRoot = Path.Combine(Root, "contracts", "release", "v1");
        foreach (var path in Directory.GetFiles(schemaRoot, "*.schema.json"))
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
            Assert.Equal("object", root.GetProperty("type").GetString());
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
            _ = JsonSchema.FromText(text, new BuildOptions { Dialect = Dialect.Draft202012 });
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
