namespace CP6.Platform.ReleaseTests;

[Collection(nameof(PackageBuildCollection))]
public sealed class P10PackageTests
{
    [Fact]
    public void Packaged_formal_publication_Schema_accepts_only_coherent_registered_versions_and_feed_identities()
    {
        P10PackageTestHarness.PackReleasePackage("0.10.2-test.schema.1", archive =>
        {
            string ReadSchema(string name)
            {
                var entry = archive.GetEntry($"contracts/release/v1/{name}");
                Assert.NotNull(entry);
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }

            var matrix = new (string Root, string Package, string Feed, bool Expected)[]
            {
                ("0.10.0", "0.10.0", "0.10.0", true),
                ("0.10.1", "0.10.1", "0.10.1", true),
                ("0.10.2", "0.10.2", "0.10.2", true),
                ("0.10.2", "0.10.1", "0.10.2", false),
                ("0.10.2", "0.10.2", "0.10.1", false),
                ("0.10.1", "0.10.2", "0.10.2", false),
                ("0.10.3", "0.10.3", "0.10.3", false),
                ("0.10.2-preview.1", "0.10.2-preview.1", "0.10.2-preview.1", false),
                ("0.10.2+build.1", "0.10.2+build.1", "0.10.2+build.1", false),
                ("00.10.2", "00.10.2", "00.10.2", false),
                ("0.10.2", "0.10.2", "0.10.2\n", false),
                ("0.10.2\n", "0.10.2\n", "0.10.2\n", false)
            };
            foreach (var item in matrix)
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(
                    ReleaseTestData.Fixture("supporting", "formal-package-publication.valid.json"))!.AsObject();
                root["version"] = item.Root;
                foreach (var packageNode in root["packages"]!.AsArray())
                {
                    var package = packageNode!.AsObject();
                    package["version"] = item.Package;
                    package["feedIdentity"] =
                        $"https://nuget.pkg.github.com/GTX537/index.json#{package["packageId"]!.GetValue<string>()}/{item.Feed}";
                }

                var result = ReleaseSchemaTestData.Evaluate(root, ReadSchema);
                Assert.True(result.IsValid == item.Expected, ReleaseSchemaTestData.Errors(result));
            }

            var wrongFeedPackage = System.Text.Json.Nodes.JsonNode.Parse(
                ReleaseTestData.Fixture("supporting", "formal-package-publication.valid.json"))!.AsObject();
            var rootVersion = wrongFeedPackage["version"]!.GetValue<string>();
            wrongFeedPackage["packages"]![0]!["feedIdentity"] =
                $"https://nuget.pkg.github.com/GTX537/index.json#CP6.Platform.Contracts/{rootVersion}";
            var wrongFeedResult = ReleaseSchemaTestData.Evaluate(wrongFeedPackage, ReadSchema);
            Assert.False(wrongFeedResult.IsValid, ReleaseSchemaTestData.Errors(wrongFeedResult));
        });
    }

    [Fact]
    public void Packaged_candidate_Schemas_accept_formal_byte_preserving_identities()
    {
        P10PackageTestHarness.PackReleasePackage("0.10.1-test.schema.1", archive =>
        {
            string ReadSchema(string name)
            {
                var entry = archive.GetEntry($"contracts/release/v1/{name}");
                Assert.NotNull(entry);
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }

            foreach (var stem in new[] { "platform", "system" })
            {
                var root = ReleaseSchemaTestData.Candidate(stem, "BytePreserving", "Rfc3161Required");
                Assert.NotEmpty(ReleaseSchemaTestData.ValidateCandidate(stem, root).PackageIds);
                var result = ReleaseSchemaTestData.Evaluate(root, ReadSchema);
                Assert.True(result.IsValid, ReleaseSchemaTestData.Errors(result));
                foreach (var mediaType in new[]
                {
                    CP6.Platform.Release.Cp6ReleaseMediaTypes.FormalPackagePublication,
                    CP6.Platform.Release.Cp6ReleaseMediaTypes.PinnedNuGetTrustStore
                })
                {
                    root["evidence"]![0]!["mediaType"] = mediaType;
                    Assert.NotEmpty(ReleaseSchemaTestData.ValidateCandidate(stem, root).PackageIds);
                    var evidenceResult = ReleaseSchemaTestData.Evaluate(root, ReadSchema);
                    Assert.True(evidenceResult.IsValid, ReleaseSchemaTestData.Errors(evidenceResult));
                }
            }
        });
    }

    [Fact]
    public void Release_package_contains_only_dll_xml_readme_and_release_contract_assets()
    {
        var entries = P10PackageTestHarness.PackReleasePackage("0.10.0-test.local.1");

        Assert.Contains("lib/net8.0/CP6.Platform.Release.dll", entries);
        Assert.All(entries, name => Assert.True(
            name is "lib/net8.0/CP6.Platform.Release.dll"
                or "lib/net8.0/CP6.Platform.Release.xml"
                or "README.md"
                or "[Content_Types].xml"
                or "CP6.Platform.Release.nuspec" ||
            name.StartsWith("contracts/release/v1/", StringComparison.Ordinal) ||
            name.StartsWith("_rels/", StringComparison.Ordinal) ||
            name.StartsWith("package/", StringComparison.Ordinal),
            $"Unexpected release package entry: {name}"));
    }

    [Fact]
    public void Test_package_set_has_exact_seven_ids_one_version_one_source_and_test_only_trust()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var manifest = P10PackageTestHarness.BuildTestSetForCurrentCommit();
        var root = manifest.RootElement;
        var packages = root.GetProperty("packages").EnumerateArray().ToArray();
        Assert.True(root.GetProperty("testOnly").GetBoolean());
        Assert.Equal(7, packages.Length);
        Assert.Single(packages.Select(package => package.GetProperty("version").GetString()).Distinct(StringComparer.Ordinal));
        Assert.Single(packages.Select(package => package.GetProperty("sourceGitSha").GetString()).Distinct(StringComparer.Ordinal));
        Assert.All(packages, package => Assert.Equal(
            "CN=CP6 Platform P10 TEST ONLY",
            package.GetProperty("certificateSubject").GetString()));
    }

    [Fact]
    public void Injected_signing_failure_removes_private_material()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        P10PackageTestHarness.AssertInjectedFailureCleansPrivateMaterial();
    }
}
