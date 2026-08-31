namespace CP6.Platform.DeploymentTests;

public sealed class P09DocumentationContractTests
{
    private static readonly string RepositoryRoot = P09ContractTestData.RepositoryRoot;

    [Fact]
    public void RuntimeGuide_RecordsCandidateOperationAndEvidenceBoundary()
    {
        var guide = ReadRequired("docs/P09-NON-PRODUCTION-RUNTIME.md");

        foreach (var required in new[]
        {
            "Implemented / Rehearsal Candidate",
            "0.9.0.0",
            "0.9.0-alpha.1",
            "P09-S01",
            "P09-S02",
            "P09-S03",
            "-P09Contract",
            "-P09Real -ExpectedGitSha",
            "eng/pack-p09.ps1 -VerifyReproducible",
            "Docker Engine API `1.49`",
            "Docker Compose `2.36.0`",
            "Passed",
            "NotRun",
            "Failed",
            "artifacts/p09-rehearsal/",
            "artifacts/p09-kubernetes/",
            "rehearsal-evidence.v1.json",
            "temporaryDirectoryRemoved",
            "docker compose down --volumes --remove-orphans --rmi local"
        })
        {
            Assert.Contains(required, guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PublicationGuide_IsAnUnexecutedExactMainRunbook()
    {
        var guide = ReadRequired("docs/P09-PUBLICATION.md");

        foreach (var required in new[]
        {
            "Publication status: Not published",
            "P09-S04: Not started",
            "exact `origin/main`",
            "CP6.Platform.Deployment",
            "0.9.0-alpha.1",
            "only this package",
            "reject overwrite",
            "P05",
            "P06",
            "P08",
            "P09",
            "package SHA-256",
            "evidence SHA-256"
        })
        {
            Assert.Contains(required, guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepositoryState_AdvancesOnlyToP09RehearsalCandidate()
    {
        Assert.Equal("0.9.0.0", ReadRequired("VERSION").Trim());

        var readme = ReadRequired("README.md");
        Assert.Contains("P09", readme, StringComparison.Ordinal);
        Assert.Contains("Implemented / Rehearsal Candidate", readme, StringComparison.Ordinal);
        Assert.Contains("docs/P09-NON-PRODUCTION-RUNTIME.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/P09-PUBLICATION.md", readme, StringComparison.Ordinal);

        var changelog = ReadRequired("CHANGELOG.md");
        Assert.Contains("## 0.9.0.0 - 2026-08-31", changelog, StringComparison.Ordinal);
        Assert.Contains("P09-S01 through P09-S03", changelog, StringComparison.Ordinal);
        Assert.Contains("Implemented / Rehearsal Candidate", changelog, StringComparison.Ordinal);
        Assert.Contains("P09-S04 through P09-S06 remain not started", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateGuides_DoNotClaimPublicationConsumptionCloudOrProduction()
    {
        var candidateGuides = string.Join(
            '\n',
            ReadRequired("docs/P09-NON-PRODUCTION-RUNTIME.md"),
            ReadRequired("docs/P09-PUBLICATION.md"));

        foreach (var forbidden in new[]
        {
            "Frozen / Consumable",
            "Published / Consumer Candidate",
            "Publication status: Published",
            "P09-S04: Complete",
            "P09-S05: Complete",
            "P09-S06: Complete",
            "production ready",
            "production deployment complete",
            "real cluster validated",
            "CRM route enabled",
            "CRM Worker enabled",
            "business Topic enabled"
        })
        {
            Assert.DoesNotContain(forbidden, candidateGuides, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ReadRequired(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Required P09 documentation file is missing: {relativePath}");
        return File.ReadAllText(path);
    }
}
