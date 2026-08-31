using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CP6.Platform.DeploymentTests;

public sealed class P09PackageTests
{
    private const string PackageVersion = "0.9.0-alpha.1";
    private static readonly string RepositoryRoot = P09ContractTestData.RepositoryRoot;
    private static readonly Lazy<PackageSnapshot> Package = new(CreatePackage);

    [Fact]
    public void PackRunner_FreezesVersionReproducibilityAndNoPublishBoundary()
    {
        var runnerPath = Path.Combine(RepositoryRoot, "eng", "pack-p09.ps1");
        Assert.True(File.Exists(runnerPath), "The exact P09 pack runner is missing.");
        var runner = File.ReadAllText(runnerPath);

        Assert.Contains("[string]$Version = '0.9.0-alpha.1'", runner, StringComparison.Ordinal);
        Assert.Contains("[string]$OutputPath = 'artifacts/p09-package'", runner, StringComparison.Ordinal);
        Assert.Contains("[switch]$VerifyReproducible", runner, StringComparison.Ordinal);
        Assert.Contains("dotnet", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--no-build", runner, StringComparison.Ordinal);
        Assert.Contains("PackageVersion", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("nuget push", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet nuget", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publish", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Package_HasExactAllowedShapeAndAllSourceContracts()
    {
        var package = Package.Value;
        var names = package.Entries.Keys.Order(StringComparer.Ordinal).ToArray();
        var expectedExact = new[]
        {
            "lib/net8.0/CP6.Platform.Deployment.dll",
            "lib/net8.0/CP6.Platform.Deployment.xml",
            "README.md",
            "[Content_Types].xml",
            "CP6.Platform.Deployment.nuspec"
        };
        foreach (var expected in expectedExact)
        {
            Assert.Contains(expected, names, StringComparer.Ordinal);
        }

        foreach (var name in names)
        {
            Assert.True(
                expectedExact.Contains(name, StringComparer.Ordinal) ||
                name.StartsWith("contracts/p09/", StringComparison.Ordinal) ||
                name.StartsWith("deploy/p09/", StringComparison.Ordinal) ||
                name.StartsWith("_rels/", StringComparison.Ordinal) ||
                name.StartsWith("package/", StringComparison.Ordinal),
                $"Unexpected package entry: {name}");
        }

        var expectedSourceEntries = ExpectedSourceEntries("contracts", "p09")
            .Concat(ExpectedSourceEntries("deploy", "p09"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var expected in expectedSourceEntries)
        {
            Assert.Contains(expected, names, StringComparer.Ordinal);
        }
        Assert.Equal(
            expectedSourceEntries,
            names.Where(name => name.StartsWith("contracts/p09/", StringComparison.Ordinal) ||
                    name.StartsWith("deploy/p09/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Package_NuspecHasExactVersionAndNoDependencies()
    {
        var nuspec = XDocument.Parse(Package.Value.Text("CP6.Platform.Deployment.nuspec"));
        var ns = nuspec.Root!.Name.Namespace;

        Assert.Equal(PackageVersion, nuspec.Descendants(ns + "version").Single().Value);
        Assert.Empty(nuspec.Descendants(ns + "dependency"));
        Assert.All(
            nuspec.Descendants(ns + "group"),
            group => Assert.Empty(group.Elements(ns + "dependency")));
    }

    [Fact]
    public void Package_ContainsNoLocalOrMutableDeliveryResidue()
    {
        var package = Package.Value;
        var forbiddenEntryPatterns = new[]
        {
            new Regex(@"(?:\A|/)\.env(?:\.|\z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"(?:\A|/)kubeconfig(?:\.|/|\z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"(?:\A|/)(?:bin|obj|artifacts|TestResults?)/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            new Regex(@"(?:\A|/)runtime/(?:secrets?|generated)/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
        };

        foreach (var name in package.Entries.Keys)
        {
            Assert.DoesNotMatch(new Regex(@"(?:\A|/)[^/]*:Zone\.Identifier\z", RegexOptions.CultureInvariant), name);
            foreach (var pattern in forbiddenEntryPatterns)
            {
                Assert.DoesNotMatch(pattern, name);
            }
        }

        foreach (var entry in package.TextEntries())
        {
            Assert.DoesNotMatch(
                new Regex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]", RegexOptions.CultureInvariant),
                entry.Text);
            Assert.DoesNotMatch(
                new Regex(@"(?:\A|[\s""'])/(?:Users|home|var/folders)/", RegexOptions.CultureInvariant),
                entry.Text);
            Assert.DoesNotContain(":latest", entry.Text, StringComparison.OrdinalIgnoreCase);

            var secretAssignments = Regex.Matches(
                entry.Text,
                @"(?i)""(?:password|token|clientSecret|apiKey)""\s*:\s*""(?<value>[^""]+)""");
            foreach (Match assignment in secretAssignments)
            {
                Assert.True(
                    entry.Name.EndsWith(".invalid.json", StringComparison.Ordinal) &&
                    assignment.Groups["value"].Value == "obvious-fake-value",
                    $"Secret-like value found in package entry '{entry.Name}'.");
            }
        }
    }

    private static PackageSnapshot CreatePackage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp6-p09-package-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(RepositoryRoot, "src", "CP6.Platform.Deployment", "CP6.Platform.Deployment.csproj");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var result = Run(
                dotnet,
                [
                    "pack",
                    project,
                    "--configuration",
                    "Release",
                    "--no-restore",
                    "--output",
                    root,
                    $"-p:PackageVersion={PackageVersion}",
                    "-p:IncludeSymbols=false"
                ]);
            Assert.True(result.ExitCode == 0, $"dotnet pack failed.{Environment.NewLine}{result.Output}{result.Error}");

            var packagePath = Directory.GetFiles(root, "*.nupkg", SearchOption.TopDirectoryOnly)
                .Single(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase));
            using var archive = ZipFile.OpenRead(packagePath);
            return new PackageSnapshot(archive.Entries.ToDictionary(
                entry => entry.FullName,
                ReadEntry,
                StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var target = new MemoryStream();
        source.CopyTo(target);
        return target.ToArray();
    }

    private static IEnumerable<string> ExpectedSourceEntries(string first, string second)
    {
        var sourceRoot = Path.Combine(RepositoryRoot, first, second);
        return Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => $"{first}/{second}/{Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')}");
    }

    private static ProcessResult Run(string fileName, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(180_000), "dotnet pack timed out.");
        return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private sealed record PackageSnapshot(IReadOnlyDictionary<string, byte[]> Entries)
    {
        public string Text(string name) => Encoding.UTF8.GetString(Entries[name]).TrimStart('\uFEFF');

        public IEnumerable<(string Name, string Text)> TextEntries() => Entries
            .Where(entry => IsTextEntry(entry.Key))
            .Select(entry => (entry.Key, Encoding.UTF8.GetString(entry.Value)));

        private static bool IsTextEntry(string name) =>
            new[] { ".json", ".yaml", ".yml", ".xml", ".nuspec", ".md", ".ps1", ".py", ".conf", ".properties" }
                .Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
