using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

namespace CP6.Platform.UnitTests;

public sealed class FoundationContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Version_UsesAuditableFourPartFormat()
    {
        var version = File.ReadAllText(Path.Combine(RepositoryRoot, "VERSION")).Trim();

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.CultureInvariant), version);
    }

    [Fact]
    public void VerificationContract_DeclaresEveryRequiredGate()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify.ps1"));
        string[] gates =
        [
            "Format",
            "Build",
            "Unit",
            "Integration",
            "Contract",
            "Security",
            "E2E",
            "Performance",
            "Migration"
        ];

        foreach (var gate in gates)
        {
            Assert.Contains($"'{gate}'", script, StringComparison.Ordinal);
        }

        Assert.Contains("summary.json", script, StringComparison.Ordinal);
        Assert.Contains("results.junit.xml", script, StringComparison.Ordinal);
        Assert.Contains("NotApplicable", script, StringComparison.Ordinal);
        Assert.Contains("'restore', $solutionPath, '--force-evaluate'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionProjects_ArePackableButContainNoRuntimeSourceAtP01()
    {
        var projects = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);

        Assert.Equal(6, projects.Length);
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            Assert.Equal("true", document.Descendants("IsPackable").Single().Value);

            var sourceFiles = Directory.GetFiles(Path.GetDirectoryName(project)!, "*.cs", SearchOption.AllDirectories)
                .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"));
            Assert.Empty(sourceFiles);
        }
    }

    [Fact]
    public void BuildBaseline_FreezesDotNetEightAndPackageIntegritySettings()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        string Property(string name) => props.Descendants(name).Single().Value;

        Assert.Equal("net8.0", Property("TargetFramework"));
        Assert.Equal("12.0", Property("LangVersion"));
        Assert.Equal("enable", Property("Nullable"));
        Assert.Equal("true", Property("TreatWarningsAsErrors"));
        Assert.Equal("true", Property("Deterministic"));
        Assert.Equal("all", Property("NuGetAuditMode"));
        Assert.Equal("git", Property("RepositoryType"));
        Assert.Equal("https://github.com/GTX537/CP6.Platform", Property("RepositoryUrl"));
        Assert.Equal("true", Property("IncludeSymbols"));
        Assert.Equal("snupkg", Property("SymbolPackageFormat"));

        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "global.json")));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        Assert.StartsWith("8.0.", sdk.GetProperty("version").GetString(), StringComparison.Ordinal);
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    [Fact]
    public void ContinuousIntegration_ExactlyCoversBothOperatingSystemsAndAllGates()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "platform-validation.yml"));
        var operatingSystems = Regex.Matches(workflow, @"^\s+-\s+(?<os>(?:ubuntu|windows)-latest)\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["os"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var gates = Regex.Matches(workflow, @"-Gate\s+(?<gate>\w+)", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["gate"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ubuntu-latest", "windows-latest"], operatingSystems);
        Assert.Equal(
            new[] { "Build", "Contract", "E2E", "Format", "Integration", "Migration", "Performance", "Security", "Unit" },
            gates);
        Assert.Contains("actions/upload-artifact@", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void P01Automation_CannotPublishAnEmptyPackage()
    {
        var automationFiles = Directory.GetFiles(Path.Combine(RepositoryRoot, ".github", "workflows"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(RepositoryRoot, "eng"), "*.ps1", SearchOption.AllDirectories));
        var automation = string.Join("\n", automationFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("nuget push", automation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packages: write", automation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-key", automation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P01DecisionRecord_PreservesDeferredScope()
    {
        var record = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "P01-FOUNDATION.md"));

        Assert.Contains("P02", record, StringComparison.Ordinal);
        Assert.Contains("P10", record, StringComparison.Ordinal);
        Assert.Contains("不发布空包", record, StringComparison.Ordinal);
        Assert.Contains("GitHub Pro", record, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CP6.Platform repository root.");
    }

    private static bool HasDirectorySegment(string path, string segment)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);
    }
}
