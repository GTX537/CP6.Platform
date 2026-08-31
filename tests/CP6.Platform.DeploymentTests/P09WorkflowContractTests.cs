using System.Text.RegularExpressions;

namespace CP6.Platform.DeploymentTests;

public sealed class P09WorkflowContractTests
{
    private static readonly string RepositoryRoot = P09ContractTestData.RepositoryRoot;
    private static readonly string Workflow = File.ReadAllText(Path.Combine(
        RepositoryRoot,
        ".github",
        "workflows",
        "platform-validation.yml"));

    [Fact]
    public void Workflow_HasBoundedUbuntuP09RuntimeJob()
    {
        var job = Job("p09-non-production-runtime");

        foreach (var required in new[]
        {
            "name: ubuntu-p09-non-production-runtime",
            "runs-on: ubuntu-latest",
            "timeout-minutes: 30",
            "permissions:",
            "contents: read",
            "actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683",
            "actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9",
            "dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj",
            "dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj",
            "tests/p09/compose-rehearsal.Tests.ps1",
            "tests/p09/cleanup-failure.Tests.ps1",
            "tests/p09/kubernetes-negative.Tests.ps1",
            "eng/test-p09-kubernetes.ps1",
            "eng/run-p09-compose-rehearsal.ps1",
            "-ExpectedGitSha ${{ github.sha }}",
            "rehearsal-evidence.v1.json",
            "overall",
            "platformGitSha",
            "temporaryDirectoryRemoved",
            "Get-FileHash",
            "if: always()",
            "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02"
        })
        {
            Assert.Contains(required, job, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("continue-on-error", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment:", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secrets.", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker login", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kubectl apply", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deploy", StepNames(job), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workflow_UploadsOnlyBoundedP09Evidence()
    {
        var job = Job("p09-non-production-runtime");
        var uploadMarker = "      - name: Preserve P09 rehearsal evidence";
        var uploadStart = job.IndexOf(uploadMarker, StringComparison.Ordinal);
        Assert.True(uploadStart >= 0, "The P09 evidence upload step is missing.");
        var upload = job[uploadStart..];
        var paths = upload.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("artifacts/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            ["artifacts/p09-rehearsal/**", "artifacts/p09-kubernetes/**"],
            paths);
        Assert.Contains("if-no-files-found: error", upload, StringComparison.Ordinal);
        Assert.Contains("retention-days: 7", upload, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingJobs_KeepP05P06P08AndRunDeploymentContractsWithoutDocker()
    {
        var validate = Job("validate");
        Assert.Contains("ubuntu-latest", validate, StringComparison.Ordinal);
        Assert.Contains("windows-latest", validate, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj",
            validate,
            StringComparison.Ordinal);
        Assert.DoesNotContain("run-p09-compose-rehearsal", validate, StringComparison.Ordinal);
        Assert.DoesNotContain("test-p09-kubernetes", validate, StringComparison.Ordinal);

        Assert.Contains("-Gate E2E -Profile ci", validate, StringComparison.Ordinal);
        Assert.Contains("-Gate Contract -Profile ci", validate, StringComparison.Ordinal);
        Assert.Contains("-Profile p05-real", Job("dapr-kafka"), StringComparison.Ordinal);
        Assert.Contains("-Profile p06-real", Job("sql-server"), StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationEntryPoint_HasExplicitP09ContractAndRealSwitches()
    {
        var verify = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify.ps1"));
        foreach (var required in new[]
        {
            "[switch]$P09Contract",
            "[switch]$P09Real",
            "[string]$ExpectedGitSha",
            "CP6.Platform.DeploymentTests.csproj",
            "compose-rehearsal.Tests.ps1",
            "cleanup-failure.Tests.ps1",
            "kubernetes-negative.Tests.ps1",
            "test-p09-kubernetes.ps1",
            "run-p09-compose-rehearsal.ps1"
        })
        {
            Assert.Contains(required, verify, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RehearsalRunner_WritesStrictCanonicalEvidenceWithoutTrailingWhitespace()
    {
        var runner = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "eng",
            "p09",
            "P09Rehearsal.psm1"));

        Assert.Contains(
            "[IO.File]::WriteAllText($evidencePath,(ConvertTo-Cp6P09CanonicalJson $evidence),",
            runner,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "(ConvertTo-Cp6P09CanonicalJson $evidence)+\"`n\"",
            runner,
            StringComparison.Ordinal);
    }

    private static string Job(string name)
    {
        var marker = $"  {name}:";
        var start = Workflow.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Workflow job '{name}' is missing.");
        var next = Regex.Match(Workflow[(start + marker.Length)..], @"(?m)^  [a-zA-Z0-9_-]+:\s*$");
        return next.Success
            ? Workflow.Substring(start, marker.Length + next.Index)
            : Workflow[start..];
    }

    private static string StepNames(string job) => string.Join(
        '\n',
        job.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("- name:", StringComparison.Ordinal)));
}
