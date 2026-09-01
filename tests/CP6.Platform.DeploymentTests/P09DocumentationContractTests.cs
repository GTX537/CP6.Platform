namespace CP6.Platform.DeploymentTests;

public sealed class P09DocumentationContractTests
{
    private static readonly string RepositoryRoot = P09ContractTestData.RepositoryRoot;

    [Fact]
    public void RuntimeGuide_RecordsFrozenConsumableFinalAuditBoundary()
    {
        var guide = ReadRequired("docs/P09-NON-PRODUCTION-RUNTIME.md");

        foreach (var required in new[]
        {
            "P09 final decision: `Frozen / Consumable`",
            "ubuntu-p09-non-production-runtime",
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
            "S01-S06 complete",
            "https://github.com/GTX537/CP6.CRM/pull/37",
            "https://github.com/GTX537/CP6.CRM/pull/38",
            "https://github.com/GTX537/CP6.CRM/pull/39",
            "https://github.com/GTX537/CP6/pull/77",
            "8578bc1df9c64b00e0f27ae602d2960a91b8450a",
            "ed08018a160d467342ddee823409232e6c412267",
            "33494115752",
            "33494115758",
            "33494115763",
            "33494115788",
            "33494115825",
            "33494116082",
            "33495318290",
            "33495318251",
            "33495318334",
            "33495318261",
            "33495318252",
            "99816026203",
            "99816026466",
            "99816026399",
            "99816026564",
            "99816026391",
            "99816026598"
        })
        {
            Assert.Contains(required, guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepositoryState_RecordsOnlyTheConditionalP09FinalDecision()
    {
        Assert.Equal("0.9.0.0", ReadRequired("VERSION").Trim());

        var readme = ReadRequired("README.md");
        Assert.Contains("P09", readme, StringComparison.Ordinal);
        Assert.Contains("P09 final decision: `Frozen / Consumable`", readme, StringComparison.Ordinal);
        Assert.Contains("until then the PR head is only a final-audit candidate", readme, StringComparison.Ordinal);
        Assert.Contains("docs/P09-NON-PRODUCTION-RUNTIME.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/P09-PUBLICATION.md", readme, StringComparison.Ordinal);

        var changelog = ReadRequired("CHANGELOG.md");
        Assert.Contains("## 0.9.0.0 - 2026-08-31", changelog, StringComparison.Ordinal);
        Assert.Contains("P09-S01 through P09-S06", changelog, StringComparison.Ordinal);
        Assert.Contains("Frozen / Consumable", changelog, StringComparison.Ordinal);
        Assert.Contains("S01-S06 complete", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalAuditGuides_DoNotClaimRuntimeCloudOrProduction()
    {
        var candidateGuides = string.Join(
            '\n',
            ReadRequired("docs/P09-NON-PRODUCTION-RUNTIME.md"),
            ReadRequired("docs/P09-PUBLICATION.md"));

        foreach (var forbidden in new[]
        {
            "| Status | `Published / Consumer Candidate` |",
            "| Current boundary | `S01-S04 complete; S05-S06 pending` |",
            "| Deferred stages | `P09-S05`, `P09-S06` |",
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
