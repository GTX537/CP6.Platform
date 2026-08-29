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
        var decisionRecord = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "P07-YARP-GATEWAY.md"));
        var changelog = File.ReadAllText(Path.Combine(RepositoryRoot, "CHANGELOG.md"));
        var props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var packageVersion = $"{props.Descendants("VersionPrefix").Single().Value}-{props.Descendants("VersionSuffix").Single().Value}";
        var verification = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify.ps1"));
        var releasePack = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "pack-release.ps1"));
        var publication = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "publish-alpha.yml"));

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.CultureInvariant), version);
        Assert.Contains($"`{version}` / package metadata", decisionRecord, StringComparison.Ordinal);
        Assert.Contains($"仓库交付版本使用四段 `VERSION`：`{version}`", decisionRecord, StringComparison.Ordinal);
        Assert.Contains($"## {version} -", changelog, StringComparison.Ordinal);
        Assert.Contains($"package metadata `{packageVersion}`", decisionRecord, StringComparison.Ordinal);
        Assert.Contains($"$packageVersion = '{packageVersion}'", verification, StringComparison.Ordinal);
        Assert.Contains($"[string]$PackageVersion = '{packageVersion}'", releasePack, StringComparison.Ordinal);
        Assert.Contains($"-PackageVersion {packageVersion}", publication, StringComparison.Ordinal);
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
    public void ProductionProjects_ArePackable_AndOnlyApprovedRuntimePackagesContainSource()
    {
        var projects = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);

        Assert.Equal(6, projects.Length);
        var projectsWithSource = new List<string>();
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            Assert.Equal("true", document.Descendants("IsPackable").Single().Value);

            var sourceFiles = Directory.GetFiles(Path.GetDirectoryName(project)!, "*.cs", SearchOption.AllDirectories)
                .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"));
            if (sourceFiles.Any())
            {
                projectsWithSource.Add(Path.GetFileNameWithoutExtension(project));
            }
        }

        Assert.Equal(
            ["CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging"],
            projectsWithSource.Order(StringComparer.Ordinal));
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
            new[] { "Build", "Contract", "E2E", "Format", "Integration", "Integration", "Integration", "Migration", "Performance", "Security", "Unit" },
            gates);
        Assert.Contains("-Profile p05-real", workflow, StringComparison.Ordinal);
        Assert.Contains("-Profile p06-real", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseAutomation_PublishesOnlyTheApprovedNonEmptyPackageSet()
    {
        var automationFiles = Directory.GetFiles(Path.Combine(RepositoryRoot, ".github", "workflows"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(RepositoryRoot, "eng"), "*.ps1", SearchOption.AllDirectories));
        var automation = string.Join("\n", automationFiles.Select(File.ReadAllText));

        Assert.Contains("nuget push", automation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("packages: write", automation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CP6.Platform.Contracts", automation, StringComparison.Ordinal);
        Assert.Contains("CP6.Platform.Abstractions", automation, StringComparison.Ordinal);
        Assert.Contains("CP6.Platform.AspNetCore", automation, StringComparison.Ordinal);
        Assert.DoesNotContain("CP6.Platform.Messaging.*.nupkg", automation, StringComparison.Ordinal);
        Assert.DoesNotContain("--skip-duplicate", automation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P01DecisionRecord_PreservesDeferredScope()
    {
        var record = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "P01-FOUNDATION.md"));

        var canonicalRoadmap = new[]
        {
            "| P02 | Abstractions + 只读 RequestContext + 无默认租户 | P01 | 单元/ASP.NET 集成测试 |",
            "| P03 | RS256/JWKS 验证、ProblemDetails、correlation | P01 | Token 负向矩阵和轮换测试 |",
            "| P04 | CloudEvents + JSON Schema + contract bundle | P01 | Schema/兼容测试和示例 |",
            "| P05 | Dapr service invocation/PubSub + Kafka conventions | P02,P04 | 真 Dapr/Kafka 集成测试 |",
            "| P06 | EF Outbox/Inbox、lease、retention、DLQ | P02,P04,P05 | kill/replay/duplicate SQL 测试 |",
            "| P07 | YARP Gateway、路由、header 清理、限流 | P03 | 直连/伪造头/路由 E2E |",
            "| P08 | OTel、健康、resiliency、Runbook | P03,P05,P06 | Trace 跨服务、故障注入 |",
            "| P09 | Compose/K8s Dapr 组件、订阅、Topic/ACL provision | P05,P08 | 非生产部署演练 |",
            "| P10 | NuGet/镜像 release、System Manifest schema、证据 | P01-P09 | 签名候选和消费方验证 |",
        };

        Assert.All(canonicalRoadmap, item => Assert.Contains(item, record, StringComparison.Ordinal));
        foreach (var id in Enumerable.Range(2, 9).Select(number => $"P{number:00}"))
        {
            Assert.Single(Regex.Matches(record, $@"^\| {id} \|", RegexOptions.Multiline | RegexOptions.CultureInvariant).Cast<Match>());
        }

        Assert.Contains("P02–P10", record, StringComparison.Ordinal);
        string[] obsoleteSemantics =
        [
            "P02 关联/审计",
            "P03 可靠事件",
            "P04 跨服务数据",
            "P05 观测",
            "P06 弹性",
            "P07 安全默认值",
            "P02 首先定义跨服务关联标识与审计契约",
        ];
        Assert.All(obsoleteSemantics, item => Assert.DoesNotContain(item, record, StringComparison.Ordinal));
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
