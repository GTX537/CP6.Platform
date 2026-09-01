namespace CP6.Platform.DeploymentTests;

public sealed class P09DocumentationContractTests
{
    private static readonly string RepositoryRoot = P09ContractTestData.RepositoryRoot;

    [Fact]
    public void RuntimeGuide_RecordsPublishedConsumerCandidateBoundary()
    {
        var guide = ReadRequired("docs/P09-NON-PRODUCTION-RUNTIME.md");

        foreach (var required in new[]
        {
            "Published / Consumer Candidate",
            "0.9.0.0",
            "0.9.0-alpha.1",
            "P09-S01",
            "P09-S02",
            "P09-S03",
            "P09-S04",
            "P09-S05",
            "P09-S06",
            "-P09Contract",
            "-P09Real -ExpectedGitSha",
            "eng/pack-p09.ps1 -VerifyReproducible",
            "Docker Engine API `1.49`",
            "Docker Compose `2.36.0`",
            "nameformat",
            "Docker DNS",
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
    public void PublicationGuide_RecordsVerifiedExactMainPublication()
    {
        var guide = ReadRequired("docs/P09-PUBLICATION.md");

        foreach (var required in new[]
        {
            "Publication status: Published and independently verified",
            "P09-S04: Complete",
            "exact `origin/main`",
            "CP6.Platform.Deployment",
            "0.9.0-alpha.1",
            "only this package",
            "reject overwrite",
            ".github/workflows/publish-p09.yml",
            "eng/p09/New-P09PublicationManifest.ps1",
            "eng/p09/Test-P09RegistryPackage.ps1",
            "P05",
            "P06",
            "P08",
            "P09",
            "package SHA-256",
            "evidence SHA-256",
            "PR #29",
            "1c40f21e38929abaaa6006f69ee70d4492890661",
            "33479175077",
            "33479705779",
            "33480300468",
            "99768201448",
            "9789925866",
            "sha256:3daad67d4a15144d5f22b64637f7e9f91bdedc4e95fec4e5e20dd09977d78f27",
            "1194316756",
            "e820d1771ed004b4a7089d008eef3bb2aca4fe35e4912d67057840373c4952cb",
            "2ffb1365e3d0cb85970e7bc148271bdc4b2ca0b37e5dbb55f772fc4f37d4bf5d",
            "S01-S04 complete; S05-S06 pending"
        })
        {
            Assert.Contains(required, guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepositoryState_AdvancesOnlyToP09PublishedConsumerCandidate()
    {
        Assert.Equal("0.9.0.0", ReadRequired("VERSION").Trim());

        var readme = ReadRequired("README.md");
        Assert.Contains("P09", readme, StringComparison.Ordinal);
        Assert.Contains("Published / Consumer Candidate", readme, StringComparison.Ordinal);
        Assert.Contains("docs/P09-NON-PRODUCTION-RUNTIME.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/P09-PUBLICATION.md", readme, StringComparison.Ordinal);

        var changelog = ReadRequired("CHANGELOG.md");
        Assert.Contains("## 0.9.0.0 - 2026-08-31", changelog, StringComparison.Ordinal);
        Assert.Contains("P09-S01 through P09-S04", changelog, StringComparison.Ordinal);
        Assert.Contains("Published / Consumer Candidate", changelog, StringComparison.Ordinal);
        Assert.Contains("P09-S05 and P09-S06 remain pending", changelog, StringComparison.Ordinal);
        Assert.Contains("S01-S04 complete; S05-S06 pending", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedGuides_DoNotClaimConsumptionCloudOrProduction()
    {
        var candidateGuides = string.Join(
            '\n',
            ReadRequired("docs/P09-NON-PRODUCTION-RUNTIME.md"),
            ReadRequired("docs/P09-PUBLICATION.md"));

        foreach (var forbidden in new[]
        {
            "Frozen / Consumable",
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
