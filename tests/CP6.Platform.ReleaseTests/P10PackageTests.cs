namespace CP6.Platform.ReleaseTests;

public sealed class P10PackageTests
{
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
