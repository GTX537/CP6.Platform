using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Platform.Release;
using CP6.Platform.Release.CapabilitiesV1;
using Json.Schema;
using Fixture = CP6.Platform.ReleaseTests.CapabilitiesV1.CapabilityValidationTests.Fixture;

namespace CP6.Platform.ReleaseTests;

[Collection(nameof(PackageBuildCollection))]
public sealed class P10FormalRegistrationTests
{
    [Theory]
    [InlineData("0.10.0", false, true)]
    [InlineData("0.10.1", false, true)]
    [InlineData("0.10.2", false, true)]
    [InlineData("0.10.3", false, true)]
    [InlineData("0.10.0", true, false)]
    [InlineData("0.10.1", true, false)]
    [InlineData("0.10.2", true, false)]
    [InlineData("0.10.3", true, true)]
    [InlineData("0.10.4", false, false)]
    [InlineData("latest", false, false)]
    [InlineData("0.10.3+build.1", true, false)]
    [InlineData("0.10.3-preview.1", true, false)]
    public void Formal_capability_version_catalog_agrees_with_source_schema(string version, bool context, bool valid)
    {
        var root = Matrix(version, context);
        AssertShape(root, valid);
    }

    [Theory]
    [InlineData("id", "S01-09")]
    [InlineData("id", "Transport")]
    [InlineData("id", "OBS")]
    [InlineData("id", "SLO")]
    [InlineData("version", "1.0.1")]
    [InlineData("profiles", "ctx-v2")]
    [InlineData("scope", "Http")]
    public void Formal_context_catalog_cannot_expand(string property, string value)
    {
        var root = Matrix("0.10.3", true);
        root["capabilities"]![0]![property] = property is "profiles" or "scope" ? new JsonArray(value) : JsonValue.Create(value);
        AssertShape(root, false);
    }

    [Fact]
    public void Formal_registration_does_not_promote_the_valid_task_local_S01_09_catalog()
    {
        var root = Matrix("0.10.3", true);
        root["capabilities"] = new JsonArray(new Fixture().Root["capabilities"]![0]!.DeepClone());
        AssertShape(root, false);
    }

    [Theory]
    [InlineData("version", "0.10.2")]
    [InlineData("feedIdentity", "https://nuget.pkg.github.com/GTX537/index.json#CP6.Platform.Abstractions/0.10.2")]
    [InlineData("feedIdentity", "https://nuget.pkg.github.com/GTX537/index.json#CP6.Platform.Contracts/0.10.3")]
    [InlineData("sourceGitSha", "main")]
    [InlineData("sha256", "invalid")]
    public void Formal_package_identity_corruption_is_rejected_by_API_and_schema(string property, string value)
    {
        var root = Matrix("0.10.3", true);
        root["packages"]![0]![property] = value;
        AssertShape(root, false);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("duplicate-hash")]
    [InlineData("archive-bytes")]
    public void Formal_semantics_reject_cross_field_and_actual_byte_corruption(string mutation)
    {
        var fixture = new Fixture(formal: true, formalVersion: "0.10.3");
        if (mutation == "source") fixture.Root["packages"]![0]!["sourceGitSha"] = new string('b', 40);
        if (mutation == "duplicate-hash") fixture.Root["packages"]![0]!["sha256"] = fixture.Root["packages"]![1]!["sha256"]!.GetValue<string>();
        if (mutation == "archive-bytes") fixture.Packages[Fixture.Ids[0]] = new byte[] { 1, 2, 3 };
        var error = Assert.Throws<Cp6ReleaseContractException>(() => fixture.Evaluate(request: "{}"u8.ToArray()));
        Assert.Equal(mutation == "source" ? "identity-mismatch" : "package-hash", error.Code);
    }

    [Theory]
    [InlineData("Test-P10FormalPrerequisites", false)]
    [InlineData("Pack-P10FormalPackages", false)]
    [InlineData("New-P10FormalPackageSet", false)]
    [InlineData("Publish-P10FormalPackageSet", false)]
    [InlineData("New-P10FormalPublicationRecord", true)]
    [InlineData("Test-P10FormalPackageSet", true)]
    public void Script_parameter_registration_is_exact_without_executing_any_writer(string name, bool reader)
    {
        var script = File.ReadAllText(Path.Combine(ReleaseTestData.RepositoryRoot, "eng/p10", name + ".ps1"));
        var parameter = name == "Test-P10FormalPackageSet" ? "ExpectedVersion" : "PackageVersion";
        var match = Regex.Match(script, @"\[ValidatePattern\('(?<pattern>[^']+)'\)\]\s*\[string\]\$" + parameter);
        Assert.True(match.Success);
        foreach (var version in new[] { "0.10.0", "0.10.1", "0.10.2", "0.10.3", "0.10.4", "latest", "0.10.3+build.1", "0.10.3-preview.1", "0.10.3\n" })
            Assert.Equal(version == "0.10.3" || reader && version is "0.10.0" or "0.10.1" or "0.10.2",
                Regex.IsMatch(version, match.Groups["pattern"].Value));
    }

    [Fact]
    public void Actual_task_local_nupkg_preserves_raw_schemas_and_positive_negative_registration()
    {
        void Inspect(ZipArchive archive)
        {
            string Read(string path)
            {
                using var data = new MemoryStream();
                using (var stream = archive.GetEntry(path)!.Open()) stream.CopyTo(data);
                Assert.Equal(File.ReadAllBytes(Path.Combine(ReleaseTestData.RepositoryRoot, path)), data.ToArray());
                return System.Text.Encoding.UTF8.GetString(data.ToArray());
            }
            var capability = JsonSchema.FromText(Read("contracts/release/capabilities/v1/capability-matrix.v1.schema.json"),
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            foreach (var item in new[] { ("0.10.0", false, true), ("0.10.1", false, true), ("0.10.2", false, true),
                ("0.10.3", true, true), ("0.10.2", true, false), ("0.10.4", true, false), ("0.10.3+build.1", true, false) })
                Assert.Equal(item.Item3, capability.Evaluate(JsonSerializer.SerializeToElement(Matrix(item.Item1, item.Item2))).IsValid);
            foreach (var versionCase in new[] { ("0.10.0", true), ("0.10.1", true), ("0.10.2", true), ("0.10.3", true), ("0.10.4", false), ("0.10.3-preview.1", false) })
            {
                var publication = JsonNode.Parse(ReleaseTestData.Fixture("supporting", "formal-package-publication.valid.json"))!;
                publication["version"] = versionCase.Item1;
                foreach (var p in publication["packages"]!.AsArray())
                {
                    p!["version"] = versionCase.Item1;
                    p["feedIdentity"] = $"https://nuget.pkg.github.com/GTX537/index.json#{p["packageId"]!.GetValue<string>()}/{versionCase.Item1}";
                }
                Assert.Equal(versionCase.Item2, ReleaseSchemaTestData.Evaluate(publication, name => Read("contracts/release/v1/" + name)).IsValid);
            }
        }

        var packagePath = Environment.GetEnvironmentVariable("CP6_FORMAL_REGISTRATION_PACKAGE");
        if (!string.IsNullOrEmpty(packagePath))
        {
            using var archive = ZipFile.OpenRead(packagePath);
            using var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            var metadata = System.Xml.Linq.XDocument.Load(nuspec).Descendants().Single(element => element.Name.LocalName == "metadata");
            var version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
            Assert.Matches(@"\A0\.10\.3-wp02\.27\.g[0-9a-f]{40}\.[1-9][0-9]*\z", version);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName == ".signature.p7s");
            Inspect(archive);
        }
        else
        {
            using var git = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = ReleaseTestData.RepositoryRoot, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true
            })!;
            var source = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            Assert.Equal(0, git.ExitCode);
            Assert.Matches(@"\A[0-9a-f]{40}\z", source);
            // This is a disposable test artifact from the working tree, not a publication of HEAD.
            P10PackageTestHarness.PackReleasePackage("0.10.3-wp02.27.g" + source + "." + DateTime.UtcNow.Ticks, Inspect);
        }
    }

    private static JsonObject Matrix(string version, bool context)
    {
        var fixture = new Fixture(formal: true, formalVersion: version);
        if (context) fixture.Root["capabilities"] = new JsonArray(new Fixture().Root["capabilities"]![1]!.DeepClone());
        return fixture.Root;
    }

    private static void AssertShape(JsonObject root, bool valid)
    {
        var exception = Record.Exception(() => Cp6CapabilityValidator.ValidateShape(Fixture.Canonical(root)));
        if (valid) Assert.Null(exception); else Assert.IsType<Cp6ReleaseContractException>(exception);
        var schema = JsonSchema.FromFile(Path.Combine(ReleaseTestData.RepositoryRoot, "contracts/release/capabilities/v1/capability-matrix.v1.schema.json"),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Assert.Equal(valid, schema.Evaluate(JsonSerializer.SerializeToElement(root)).IsValid);
    }
}
