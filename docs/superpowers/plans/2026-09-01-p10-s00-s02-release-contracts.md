# P10 S00-S02 Platform Release Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the independent `CP6.Platform.Release` package, deterministic release contracts and validators, then produce one immutable seven-package test-only artifact for the CRM S03 handoff without publishing a formal package or candidate.

**Architecture:** `CP6.Platform.Release` is an independent, side-effect-free .NET 8 package that owns canonical JSON, primary candidate schemas, supporting evidence schemas, strict validation, trust-policy selection, and Locator key derivation. S02 builds all seven packages from one exact Platform SHA, signs them with an ephemeral test-only certificate, and transports them through two GitHub Actions v4 artifacts: a package artifact followed by a transport-record artifact that binds the first artifact's ID and digest. No formal feed, GHCR image, R2 Locator, CRM runtime, or deployment path is created.

**Tech Stack:** .NET 8/C# 12, `System.Text.Json`, xUnit 2.9.3, JsonSchema.Net 9.4.0 in tests only, PowerShell 7, NuGet signed-package CLI, GitHub Actions, GitHub Actions artifact v4.

---

## Scope check

This plan covers one testable Platform producer subsystem:

- P10-S00: package boundary, primary/supporting schemas, fixed identifiers, and contract assets;
- P10-S01: `DeterministicJsonProfile.v1`, strict raw-byte parsing, contract validation, trust-policy selection, and golden fixtures;
- P10-S02: one-build seven-package test set, ephemeral test signing, immutable package artifact, and post-upload transport record.

These dependent stages require identities that do not exist before this plan completes and therefore receive separate plans:

- P10-S03: CRM consumes the exact S02 workflow run and artifact IDs;
- P10-S04: Platform formal X.509/RFC3161 signing and GitHub Packages publication;
- P10-S05: CRM consumes the exact S04 package version and published hashes;
- P10-S06: public CP6 validation/publication workflows, Cloudflare R2 objects, cosign bundle, Locator commit, and R00 erratum.

The S00-S02 state ceiling is `Implemented / Test Candidate`. It is not `Frozen / Consumable`, `Published-Unconfirmed`, or deployable.

## Fixed values

| Contract | Exact value |
|---|---|
| Repository delivery version | `0.10.0.0` |
| Release package default development version | `0.10.0-test.local.1` |
| S02 version pattern | `0.10.0-test.<12-lowercase-source-sha>.<run-attempt>` |
| Runtime package IDs | Contracts, Abstractions, AspNetCore, Messaging, EntityFramework |
| Additional package IDs | Deployment, Release |
| Test package count | 7 `.nupkg` plus 7 `.snupkg` |
| Test certificate subject | `CN=CP6 Platform P10 TEST ONLY` |
| S02 signing runner | `windows-latest`; no operating-system certificate-store mutation |
| Contract root | `contracts/release/v1/` |
| JSON profile | `cp6-deterministic-json-v1` |
| JSON maximum bytes/depth | 4 MiB / 32 |
| Storage authority ID | `cp6-release-r2-v1` |
| Package artifact | `p10-s02-packages-<sha>-<attempt>` |
| Transport artifact | `p10-s02-transport-<sha>-<attempt>` |
| Artifact retention | 90 days |
| Formal package destination | None in S00-S02 |
| Candidate deployability | `candidateKind=PlatformReference`, `deployable=false` |

## Task 0: Land the approved design and create the implementation worktree

**Files:**

- Existing design branch: `docs/superpowers/specs/2026-09-01-p10-release-governance-design.md`
- Existing design branch: `docs/superpowers/plans/2026-09-01-p10-s00-s02-release-contracts.md`
- New worktree: `D:\CP6.Platform-worktrees\p10-release-contracts`

- [ ] **Step 1: Verify that the design branch contains documentation only**

```powershell
git status --short
git diff --check
git diff origin/main...HEAD --stat
git diff origin/main...HEAD -- src tests contracts eng .github CP6.Platform.sln VERSION
```

Expected: clean status after committing this plan; the last command has no output; the branch diff contains only the approved spec and this plan.

- [ ] **Step 2: Push the design branch and open its PR**

```powershell
git push -u origin codex/p10-release-governance-design
$designPrUrl = gh pr create --repo GTX537/CP6.Platform --base main --head codex/p10-release-governance-design --title "docs: approve P10 release governance" --body "Approves the P10 producer-first release contracts, deterministic bytes, seven-package test transport, formal trust gates, Cloudflare-native evidence semantics, and S00-S02 implementation plan. No package publication, R2 write, CRM change, or deployment is included."
$designPrNumber = gh pr view $designPrUrl --repo GTX537/CP6.Platform --json number --jq .number
gh pr checks $designPrNumber --repo GTX537/CP6.Platform --watch
```

Expected: every required check passes. Do not merge a failing documentation branch.

- [ ] **Step 3: Merge the design PR and capture exact remote main**

```powershell
gh pr merge $designPrNumber --repo GTX537/CP6.Platform --merge
git fetch origin main --prune
$designMainSha = (git rev-parse origin/main).Trim()
gh pr view $designPrNumber --repo GTX537/CP6.Platform --json state,mergeCommit --jq '{state:.state,sha:.mergeCommit.oid}'
```

Expected: PR state `MERGED`; its merge SHA is contained in the current `origin/main`. If another authorized commit landed, inspect it and keep the newest verified `origin/main` as the baseline.

- [ ] **Step 4: Create one clean implementation worktree**

Run from `D:\CP6\CP6.Platform`:

```powershell
$implementationWorktree = 'D:\CP6.Platform-worktrees\p10-release-contracts'
git fetch origin main --prune
git worktree add -b codex/p10-s00-s02-release-contracts $implementationWorktree origin/main
git -C $implementationWorktree status --short --branch
git -C $implementationWorktree rev-parse HEAD
```

Expected: a clean branch at the verified `origin/main`. Do not reuse, clean, reset, or stash another worktree.

- [ ] **Step 5: Establish the baseline on a machine with .NET 8**

```powershell
dotnet --list-sdks
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Build -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
```

Expected: .NET 8 is listed and all three gates pass. The current planning host has only .NET 10, so it is not valid baseline evidence. If no .NET 8 host is available, stop and use the repository CI matrix; do not edit `global.json` to disguise the environment problem.

## Task 1: Establish the independent Release package and test boundary

**Files:**

- Create: `src/CP6.Platform.Release/CP6.Platform.Release.csproj`
- Create: `src/CP6.Platform.Release/Cp6ReleaseContractIds.cs`
- Create: `tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj`
- Create: `tests/CP6.Platform.ReleaseTests/ReleaseProjectBoundaryTests.cs`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`
- Modify: `CP6.Platform.sln`

- [ ] **Step 1: Write the failing repository-boundary assertions**

Add the Release package to `ExpectedDependencies`:

```csharp
["CP6.Platform.Release"] = [],
```

Add this test to `RepositoryArchitectureTests.cs`:

```csharp
[Fact]
public void Release_package_is_independent_and_owns_only_release_contract_assets()
{
    var projects = LoadProjects();
    var release = projects["CP6.Platform.Release"].Document;

    Assert.Empty(release.Descendants("ProjectReference"));
    Assert.Empty(release.Descendants("PackageReference"));
    Assert.Empty(release.Descendants("FrameworkReference"));

    var packed = GetProjectItems(release)
        .Where(item => string.Equals(GetItemValue(item, "Pack"), "true", StringComparison.OrdinalIgnoreCase))
        .Select(item => (item.Attribute("Include")?.Value, GetItemValue(item, "PackagePath")))
        .ToArray();
    Assert.Equal(
        [("../../contracts/release/v1/**/*", "contracts/release/v1/%(RecursiveDir)%(Filename)%(Extension)")],
        packed);

    foreach (var (packageId, project) in projects.Where(project => project.Key != "CP6.Platform.Release"))
    {
        Assert.DoesNotContain(
            GetProjectItems(project.Document),
            item => (item.Attribute("Include")?.Value ?? string.Empty)
                .Contains("contracts/release", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            project.Document.Descendants("ProjectReference"),
            reference => string.Equals(
                Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value),
                "CP6.Platform.Release",
                StringComparison.Ordinal));
    }
}
```

The existing `P08_PackageEvidenceAndProductionSafetyGuards_AreEncoded` test enumerates every packable source project. Add this branch immediately before its final `else` so the new project does not get mistaken for an asset-free P08 package while all historical P08 assertions remain unchanged:

```csharp
else if (packageId == "CP6.Platform.Release")
{
    Assert.Equal(["contracts/release/v1/%(RecursiveDir)%(Filename)%(Extension)"], packedAssets);
}
```

Create `ReleaseProjectBoundaryTests.cs`:

```csharp
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class ReleaseProjectBoundaryTests
{
    [Fact]
    public void Assembly_has_the_expected_public_identity()
    {
        Assert.Equal("CP6.Platform.Release", typeof(Cp6ReleaseContractIds).Assembly.GetName().Name);
    }

    [Fact]
    public void Contract_ids_are_exact_and_unique()
    {
        Assert.Equal(10, Cp6ReleaseContractIds.All.Count);
        Assert.Equal(Cp6ReleaseContractIds.All.Count, Cp6ReleaseContractIds.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Cp6ReleaseContractIds.All, id => Assert.StartsWith("https://schemas.cp6.dev/release/", id, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run focused tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~Release_package_is_independent"
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release
```

Expected: first test fails because the Release project is missing; second command fails because the test project is missing.

- [ ] **Step 3: Create the package project**

Create `CP6.Platform.Release.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CP6.Platform.Release</AssemblyName>
    <RootNamespace>CP6.Platform.Release</RootNamespace>
    <Description>Deterministic release, evidence, trust, and candidate contracts for CP6.</Description>
    <PackageId>CP6.Platform.Release</PackageId>
    <IsPackable>true</IsPackable>
    <VersionPrefix>0.10.0</VersionPrefix>
    <VersionSuffix>test.local.1</VersionSuffix>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <None Include="../../contracts/release/v1/**/*"
          Pack="true"
          PackagePath="contracts/release/v1/%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

Create `CP6.Platform.ReleaseTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="JsonSchema.Net" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/CP6.Platform.Release/CP6.Platform.Release.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Create `Cp6ReleaseContractIds.cs`:

```csharp
namespace CP6.Platform.Release;

public static class Cp6ReleaseContractIds
{
    public const string Common = "https://schemas.cp6.dev/release/release-common.v1";
    public const string SystemManifest = "https://schemas.cp6.dev/release/system-release-manifest.v1";
    public const string CandidateResult = "https://schemas.cp6.dev/release/candidate-result.v2";
    public const string CandidateLocator = "https://schemas.cp6.dev/release/candidate-locator.v1";
    public const string PlatformCandidate = "https://schemas.cp6.dev/release/platform-release-candidate.v1";
    public const string ReleaseGateResult = "https://schemas.cp6.dev/release/release-gate-result.v1";
    public const string SystemLineageBootstrap = "https://schemas.cp6.dev/release/system-lineage-bootstrap-evidence.v1";
    public const string EvidenceRecord = "https://schemas.cp6.dev/release/evidence-record.v1";
    public const string BuildProvenance = "https://schemas.cp6.dev/release/build-invocation-provenance.v1";
    public const string TestPackageTransport = "https://schemas.cp6.dev/release/test-package-transport.v1";
    public const string PinnedTrustStore = "https://schemas.cp6.dev/release/pinned-trust-store.v1";

    public static IReadOnlyList<string> All { get; } =
    [
        BuildProvenance, CandidateLocator, CandidateResult, EvidenceRecord, PinnedTrustStore,
        PlatformCandidate, ReleaseGateResult, SystemLineageBootstrap, SystemManifest, TestPackageTransport
    ];
}
```

Add both projects to the solution under the existing `src` and `tests` solution folders. Use `dotnet sln` and then inspect the resulting solution diff rather than hand-editing project configuration GUIDs:

```powershell
dotnet sln CP6.Platform.sln add src/CP6.Platform.Release/CP6.Platform.Release.csproj --solution-folder src
dotnet sln CP6.Platform.sln add tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --solution-folder tests
```

- [ ] **Step 4: Run the focused tests and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~Release_package_is_independent|FullyQualifiedName~SourceProjects_ExactlyMatchApprovedRuntimeAndTestingSet|FullyQualifiedName~ProjectReferences_ExactlyMatchApprovedDependencyDirection"
```

Expected: PASS.

- [ ] **Step 5: Commit the package boundary**

```powershell
git add -- CP6.Platform.sln src/CP6.Platform.Release tests/CP6.Platform.ReleaseTests tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git diff --cached --check
git commit -m "feat(release): establish P10 package boundary"
```

## Task 2: Add the exact primary and supporting schema assets

**Files:**

- Create: `contracts/release/v1/release-common.v1.schema.json`
- Create: `contracts/release/v1/system-release-manifest.v1.schema.json`
- Create: `contracts/release/v1/candidate-result.v2.schema.json`
- Create: `contracts/release/v1/candidate-locator.v1.schema.json`
- Create: `contracts/release/v1/platform-release-candidate.v1.schema.json`
- Create: `contracts/release/v1/release-gate-result.v1.schema.json`
- Create: `contracts/release/v1/system-lineage-bootstrap-evidence.v1.schema.json`
- Create: `contracts/release/v1/evidence-record.v1.schema.json`
- Create: `contracts/release/v1/build-invocation-provenance.v1.schema.json`
- Create: `contracts/release/v1/test-package-transport.v1.schema.json`
- Create: `contracts/release/v1/pinned-trust-store.v1.schema.json`
- Create: `contracts/release/v1/assets.v1.json`
- Create: `tests/CP6.Platform.ReleaseTests/ReleaseSchemaAssetTests.cs`

- [ ] **Step 1: Write the failing asset test**

Create `ReleaseSchemaAssetTests.cs`:

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using Json.Schema;

namespace CP6.Platform.ReleaseTests;

public sealed class ReleaseSchemaAssetTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Asset_manifest_lists_all_contracts_once_in_ordinal_order()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "contracts", "release", "v1", "assets.v1.json")));
        var common = document.RootElement.GetProperty("commonSchema");
        Assert.Equal(Cp6ReleaseContractIds.Common, common.GetProperty("id").GetString());
        Assert.Equal("release-common.v1.schema.json", common.GetProperty("path").GetString());
        Assert.Equal("application/schema+json", common.GetProperty("mediaType").GetString());
        var assets = document.RootElement.GetProperty("schemas").EnumerateArray().ToArray();
        Assert.Equal(Cp6ReleaseContractIds.All, assets.Select(asset => asset.GetProperty("id").GetString()).ToArray());
        Assert.Equal(assets.Length, assets.Select(asset => asset.GetProperty("path").GetString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_schema_is_draft_2020_12_closed_and_buildable()
    {
        var schemaRoot = Path.Combine(Root, "contracts", "release", "v1");
        foreach (var path in Directory.GetFiles(schemaRoot, "*.schema.json"))
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
            Assert.Equal("object", root.GetProperty("type").GetString());
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
            _ = JsonSchema.FromText(text, new BuildOptions { Dialect = Dialect.Draft202012 });
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
```

- [ ] **Step 2: Run the asset tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~ReleaseSchemaAssetTests
```

Expected: FAIL because `contracts/release/v1` does not exist.

- [ ] **Step 3: Create the shared definitions schema**

`release-common.v1.schema.json` is a closed root object whose `$defs` contain these exact definitions:

| `$defs` name | Exact rule |
|---|---|
| `sha256` | string `^[0-9a-f]{64}$` |
| `gitSha` | string `^[0-9a-f]{40}$` |
| `utcTimestamp` | string `^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3}Z$` |
| `releaseTag` | string matching the approved `vMAJOR.MINOR.PATCH[-prerelease]` grammar |
| `storageAuthority` | const `cp6-release-r2-v1` |
| `objectReference` | closed object requiring `storageAuthority`, `key`, `mediaType`, `sha256`, `byteLength`; key matches the fixed content-addressed grammar; byte length is 1..4 MiB |
| `packageIdentity` | closed object requiring package ID/version, author-signed hash, published hash, feed identity/transformation, signer fingerprint, timestamp policy |
| `workflowIdentity` | closed object requiring repository, workflow path, workflow-file SHA, commit SHA, run ID, run attempt, environment |
| `evidenceSubject` | closed object requiring subject kind, subject name, SHA-256 or OCI digest, source Git SHA |

Use `$id=https://schemas.cp6.dev/release/release-common.v1`, `type=object`, `properties={}`, and `additionalProperties=false`. All other schema files reference definitions using the absolute `$id` plus `#/$defs/<name>`.

The object key grammar is `^objects/sha256/[0-9a-f]{2}/[0-9a-f]{64}/[a-z0-9][a-z0-9.-]{0,127}\.json$`. The closed media-type enum is exactly:

```text
application/vnd.cp6.system-release-manifest.v1+json
application/vnd.cp6.candidate-result.v2+json
application/vnd.cp6.candidate-locator.v1+json
application/vnd.cp6.platform-release-candidate.v1+json
application/vnd.cp6.release-gate-result.v1+json
application/vnd.cp6.system-lineage-bootstrap-evidence.v1+json
application/vnd.cp6.evidence-record.v1+json
application/vnd.cp6.build-invocation-provenance.v1+json
application/vnd.cp6.test-package-transport.v1+json
application/vnd.cp6.pinned-trust-store.v1+json
application/vnd.oai.openapi+json;version=3.1
application/schema+json
application/spdx+json
application/vnd.cyclonedx+json
application/sarif+json
application/vnd.in-toto+json
application/vnd.dev.sigstore.bundle.v0.3+json
```

- [ ] **Step 4: Create the ten contract schemas from the fixed matrix**

Every row below is a closed Draft 2020-12 root object. Every listed property is required unless the row explicitly marks it nullable.

| Schema | Required root properties | Fixed discriminators and invariants encoded in Schema |
|---|---|---|
| `system-release-manifest.v1` | `$schemaId`, `candidateKind`, `deployable`, `createdAtUtc`, `repositories`, `packages`, `images`, `compatibility`, `evidence`, `lineage` | `candidateKind=System`, `deployable=true`, repositories contain exactly CP6/Platform/CRM/Portal |
| `candidate-result.v2` | `$schemaId`, `releaseTag`, `repositories`, `systemManifest`, `releaseGateResult`, `validationWorkflow`, `trustPolicyVersion`, `evidencePolicyVersion` | four exact repos; validation conclusion `Success`; no publication final conclusion field |
| `candidate-locator.v1` | `$schemaId`, `releaseTag`, `subjectKind`, `subject`, `trustPolicyVersion`, `signerKeyId`, `createdAtUtc` | subject kind enum `SystemCandidateResult` or `PlatformReleaseCandidate`; key ID `^sha256:[0-9a-f]{64}$` |
| `platform-release-candidate.v1` | `$schemaId`, `candidateKind`, `deployable`, `createdAtUtc`, `platformSource`, `packages`, `buildProvenance`, `images`, `evidence`, `crmConsumer`, `publisher`, `verifier`, `releaseGateResult`, `policyVersions` | `candidateKind=PlatformReference`, `deployable=false`, exactly seven unique package IDs |
| `release-gate-result.v1` | `$schemaId`, `createdAtUtc`, `workflow`, `inputSubjects`, `gates`, `conclusion` | conclusion enum `Success` or `Failure`; every gate has a subject binding |
| `system-lineage-bootstrap-evidence.v1` | `$schemaId`, `createdAtUtc`, `authority`, `systemManifestSubject`, `reason`, `trustPolicyVersion`, `signaturePolicy` | bootstrap purpose only; reason length 1..4096 |
| `evidence-record.v1` | `$schemaId`, `createdAtUtc`, `evidenceKind`, `producer`, `policyVersion`, `accessClass`, `object`, `subjects`, `conclusion` | access class enum `RequiredPublic`, `RestrictedAudit`, `TestOnly`; nonempty subjects |
| `build-invocation-provenance.v1` | `$schemaId`, `createdAtUtc`, `sourceGitSha`, `buildInvocationId`, `toolchain`, `preSignOutputs`, `finalPackages` | seven unique final package subjects map to pre-sign outputs |
| `test-package-transport.v1` | `$schemaId`, `testOnly`, `platformSourceSha`, `workflow`, `packageArtifact`, `createdAtUtc`, `expiresAtUtc` | `testOnly=true`; artifact ID/run/attempt positive integers; digest `sha256:<64 hex>` |
| `pinned-trust-store.v1` | `$schemaId`, `policyVersion`, `minimumAcceptedPolicyVersion`, `acceptedHistoricalPolicyVersions`, `storageAuthorities`, `keys` | fixed R2 authority; key purpose enum `oci`/`candidate-locator`; revocation reason required with `revokedAt` |

Set each schema `$id` to its `Cp6ReleaseContractIds` value and `additionalProperties=false`. Nested objects are closed as well. Arrays representing sets use `uniqueItems=true`; semantic ordinal ordering is enforced by S01 validation.

- [ ] **Step 5: Create the asset manifest**

`assets.v1.json` contains `profile=cp6-deterministic-json-v1`, one `commonSchema` entry for `release-common.v1.schema.json`, and ten `schemas` entries sorted by schema ID. Every schema entry contains exact `id`, relative `path`, and media type `application/schema+json`. Do not place mutable URLs, hashes that must be manually refreshed, or environment identities in this source manifest.

- [ ] **Step 6: Run schema tests and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~ReleaseSchemaAssetTests
```

Expected: PASS and all eleven schema files parse as Draft 2020-12.

- [ ] **Step 7: Commit the schema assets**

```powershell
git add -- contracts/release/v1 tests/CP6.Platform.ReleaseTests/ReleaseSchemaAssetTests.cs
git diff --cached --check
git commit -m "feat(release): define P10 candidate schemas"
```

## Task 3: Implement `DeterministicJsonProfile.v1`

**Files:**

- Create: `src/CP6.Platform.Release/Cp6ReleaseContractException.cs`
- Create: `src/CP6.Platform.Release/Cp6DeterministicJson.cs`
- Create: `tests/CP6.Platform.ReleaseTests/DeterministicJsonTests.cs`
- Create: `contracts/release/v1/fixtures/deterministic/simple.input.json`
- Create: `contracts/release/v1/fixtures/deterministic/simple.canonical.json`

- [ ] **Step 1: Write the failing deterministic-byte tests**

```csharp
using System.Text;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class DeterministicJsonTests
{
    [Fact]
    public void Canonicalize_sorts_properties_and_emits_exact_utf8_without_newline()
    {
        var actual = Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"z\":2,\"a\":\"é\",\"control\":\"\\n\"}"));
        var expected = Encoding.UTF8.GetBytes("{\"a\":\"é\",\"control\":\"\\u000a\",\"z\":2}");
        Assert.Equal(expected, actual);
        Assert.NotEqual((byte)'\n', actual[^1]);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}", "duplicate-property")]
    [InlineData("{\"a\":1,}", "invalid-json")]
    [InlineData("/*x*/{\"a\":1}", "invalid-json")]
    [InlineData("{\"n\":-1}", "number-format")]
    [InlineData("{\"n\":1.0}", "number-format")]
    [InlineData("{\"n\":1e0}", "number-format")]
    public void Canonicalize_rejects_non_profile_json(string json, string code)
    {
        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Canonicalize_rejects_bom_non_nfc_and_resource_limit_violations()
    {
        Assert.Equal("utf8-bom", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize([0xef, 0xbb, 0xbf, (byte)'{', (byte)'}'])).Code);
        Assert.Equal("unicode-normalization", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"v\":\"é\"}"))).Code);
        Assert.Equal("object-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(new byte[4 * 1024 * 1024 + 1])).Code);
    }
}
```

Add focused tests for the remaining exact boundaries: malformed UTF-8 → `invalid-utf8`; escaped unpaired `\uD800` → `unicode-scalar`; root array → `root-object`; nesting depth 33 → `depth-limit`; 257 object members → `member-limit`; 4097 nested array items → `array-limit`; a string of 65,537 UTF-8 bytes → `string-limit`; `9223372036854775807` accepted; and `9223372036854775808` → `integer-range`. Construct the large values in memory so fixtures stay small.

- [ ] **Step 2: Run tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~DeterministicJsonTests
```

Expected: FAIL because the exception and canonicalizer do not exist.

- [ ] **Step 3: Implement the contract exception**

```csharp
namespace CP6.Platform.Release;

public sealed class Cp6ReleaseContractException : Exception
{
    public Cp6ReleaseContractException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
```

- [ ] **Step 4: Implement strict parsing and canonical writing**

`Cp6DeterministicJson` exposes only:

```csharp
public static class Cp6DeterministicJson
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const int MaximumDepth = 32;
    public const int MaximumMembers = 256;
    public const int MaximumArrayItems = 4096;
    public const int MaximumStringUtf8Bytes = 65_536;

    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json);
    public static string Sha256Hex(ReadOnlySpan<byte> value);
}
```

Implement `Canonicalize` in this exact order:

1. reject length over 4 MiB and UTF-8 BOM;
2. use strict `UTF8Encoding(false, true)` and `Utf8JsonReader` with comments/trailing commas disabled and depth 32;
3. track a `HashSet<string>(StringComparer.Ordinal)` per object and reject duplicate names before creating a `JsonDocument`;
4. count object members/array entries and reject configured limits;
5. reject non-NFC names/values, unpaired surrogates, negative numbers, fractions, exponents, and integers over `long.MaxValue`;
6. parse one root object;
7. recursively sort object members with `StringComparer.Ordinal`, preserve array order, and write integers in invariant base 10;
8. write strings manually so quotes/backslashes are escaped and every U+0000-U+001F value becomes lowercase `\u00xx`; and
9. return raw UTF-8 bytes without BOM, whitespace, or final newline.

Do not reuse `Cp6P09Json`: its number and escaping semantics are deliberately broader than P10.

- [ ] **Step 5: Add cross-process golden fixtures**

`simple.input.json` is the unsorted input from the first test. `simple.canonical.json` contains exactly the 35 UTF-8 bytes for `{"a":"é","control":"\u000a","z":2}` with no BOM or trailing newline. Add a test that reads the input, canonicalizes it, compares the file bytes, and asserts lowercase SHA-256 `c70d0dc4eaf50a576944851115bb0e81a935fc39c7893a992a6dd00092eafdb1`.

- [ ] **Step 6: Run tests and confirm GREEN on Windows and Linux**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~DeterministicJsonTests
```

Expected: PASS locally and byte-identical SHA-256 in both CI matrix jobs.

- [ ] **Step 7: Commit deterministic JSON**

```powershell
git add -- src/CP6.Platform.Release/Cp6ReleaseContractException.cs src/CP6.Platform.Release/Cp6DeterministicJson.cs tests/CP6.Platform.ReleaseTests/DeterministicJsonTests.cs contracts/release/v1/fixtures/deterministic
git diff --cached --check
git commit -m "feat(release): add deterministic JSON profile"
```

## Task 4: Implement primary candidate validation and lane separation

**Files:**

- Create: `src/CP6.Platform.Release/Cp6ValidatedReleaseDocument.cs`
- Create: `src/CP6.Platform.Release/Cp6ReleaseMediaTypes.cs`
- Create: `src/CP6.Platform.Release/Cp6ReleaseJsonRules.cs`
- Create: `src/CP6.Platform.Release/Cp6ReleaseValidator.cs`
- Create: `tests/CP6.Platform.ReleaseTests/ReleaseTestData.cs`
- Create: `tests/CP6.Platform.ReleaseTests/PrimaryCandidateValidationTests.cs`
- Create: `contracts/release/v1/fixtures/primary/system.valid.json`
- Create: `contracts/release/v1/fixtures/primary/candidate-result.valid.json`
- Create: `contracts/release/v1/fixtures/primary/candidate-locator-system.valid.json`
- Create: `contracts/release/v1/fixtures/primary/candidate-locator-platform.valid.json`
- Create: `contracts/release/v1/fixtures/primary/platform.valid.json`
- Create: `contracts/release/v1/fixtures/primary/platform-as-system.invalid.json`
- Create: `contracts/release/v1/fixtures/primary/system-missing-portal.invalid.json`
- Create: `contracts/release/v1/fixtures/primary/platform-mixed-package-version.invalid.json`
- Create: `contracts/release/v1/fixtures/primary/mutations/*.json`

- [ ] **Step 1: Write failing lane-separation tests**

```csharp
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class PrimaryCandidateValidationTests
{
    [Fact]
    public void Platform_fixture_has_exact_non_deployable_seven_package_identity()
    {
        var document = Cp6ReleaseValidator.ValidatePlatformCandidate(ReleaseTestData.Fixture("primary", "platform.valid.json"));
        Assert.Equal("PlatformReference", document.CandidateKind);
        Assert.False(document.Deployable);
        Assert.Equal(7, document.PackageIds.Count);
        Assert.Equal(document.PackageIds.Order(StringComparer.Ordinal), document.PackageIds);
    }

    [Fact]
    public void System_validator_requires_four_exact_repositories_and_deployable_true()
    {
        var document = Cp6ReleaseValidator.ValidateSystemCandidate(ReleaseTestData.Fixture("primary", "system.valid.json"));
        Assert.True(document.Deployable);
        Assert.Equal(["CP6", "CP6.CRM", "CP6.Platform", "CP6.Portal"], document.RepositoryNames);
        Assert.Equal("repository-set", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidateSystemCandidate(ReleaseTestData.Fixture("primary", "system-missing-portal.invalid.json"))).Code);
    }

    [Fact]
    public void Candidate_lanes_cannot_be_substituted()
    {
        Assert.Equal("candidate-kind", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidateSystemCandidate(ReleaseTestData.Fixture("primary", "platform-as-system.invalid.json"))).Code);
        Assert.Equal("candidate-kind", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6ReleaseValidator.ValidatePlatformCandidate(ReleaseTestData.Fixture("primary", "system.valid.json"))).Code);
    }
}
```

Add a `MemberData` matrix with one row for each of the four entry points. Each row supplies its valid fixture plus three single-mutation fixtures; place the unknown property in the deepest required nested object and mutate required root properties for the other two cases. Assert these exact codes:

| Entry point | Valid fixture | Missing required property | Unknown property | Wrong JSON kind |
|---|---|---|---|---|
| `ValidateSystemCandidate` | `system.valid.json` | `mutations/system-missing.invalid.json` → `missing-property` | `mutations/system-unknown.invalid.json` → `unknown-property` | `mutations/system-wrong-kind.invalid.json` → `property-kind` |
| `ValidateCandidateResult` | `candidate-result.valid.json` | `mutations/candidate-result-missing.invalid.json` → `missing-property` | `mutations/candidate-result-unknown.invalid.json` → `unknown-property` | `mutations/candidate-result-wrong-kind.invalid.json` → `property-kind` |
| `ValidateCandidateLocator` | `candidate-locator-platform.valid.json` | `mutations/candidate-locator-missing.invalid.json` → `missing-property` | `mutations/candidate-locator-unknown.invalid.json` → `unknown-property` | `mutations/candidate-locator-wrong-kind.invalid.json` → `property-kind` |
| `ValidatePlatformCandidate` | `platform.valid.json` | `mutations/platform-missing.invalid.json` → `missing-property` | `mutations/platform-unknown.invalid.json` → `unknown-property` | `mutations/platform-wrong-kind.invalid.json` → `property-kind` |

Also validate both Locator positives and assert their subject discriminators are `SystemCandidateResult` and `PlatformReleaseCandidate` respectively.

Create the shared fixture helper exactly once and use it from Tasks 4-5; it reads raw bytes and never normalizes them:

```csharp
namespace CP6.Platform.ReleaseTests;

internal static class ReleaseTestData
{
    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    public static byte[] Fixture(string group, string name) =>
        File.ReadAllBytes(Path.Combine(Root.Value, "contracts", "release", "v1", "fixtures", group, name));

    public static string RepositoryRoot => Root.Value;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CP6.Platform.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate CP6.Platform.sln from the test output directory.");
    }
}
```

- [ ] **Step 2: Run focused tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~PrimaryCandidateValidationTests
```

Expected: FAIL because validators and fixtures do not exist.

- [ ] **Step 3: Implement the validated result type**

```csharp
namespace CP6.Platform.Release;

public sealed record Cp6ValidatedReleaseDocument(
    string SchemaId,
    string? CandidateKind,
    string? SubjectKind,
    bool? Deployable,
    string Sha256,
    IReadOnlyList<string> RepositoryNames,
    IReadOnlyList<string> PackageIds,
    IReadOnlyList<string> SubjectHashes,
    byte[] CanonicalUtf8);
```

`Cp6ReleaseMediaTypes` exposes one `public const string` per media type from Task 2 plus an ordinal-sorted, read-only `All` collection. Add a boundary assertion that `All` contains exactly 17 unique values and that every object-reference fixture uses a member of this collection.

- [ ] **Step 4: Implement exact-object validation helpers**

`Cp6ReleaseJsonRules` is internal and supplies these concrete operations:

```csharp
internal static void RequireExactObject(JsonElement value, params string[] expectedProperties);
internal static JsonElement RequireProperty(JsonElement value, string name, JsonValueKind kind);
internal static string RequireString(JsonElement value, string name, string code);
internal static bool RequireBoolean(JsonElement value, string name, string code);
internal static long RequireNonNegativeInteger(JsonElement value, string name, string code);
internal static void RequireOrdinalSet(IReadOnlyList<string> values, string code);
internal static void RequireSha256(string value, string code);
internal static void RequireGitSha(string value, string code);
internal static void RequireUtcMilliseconds(string value, string code);
```

Every exception has a stable lowercase kebab-case code. Do not silently ignore unknown fields or reorder an input set to make it valid.

- [ ] **Step 5: Implement the primary entry points**

`Cp6ReleaseValidator` exposes:

```csharp
public static Cp6ValidatedReleaseDocument ValidateSystemCandidate(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidateCandidateResult(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidateCandidateLocator(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidatePlatformCandidate(ReadOnlySpan<byte> utf8Json);
```

Each method:

1. validates the raw bytes with `Cp6DeterministicJson.Canonicalize`;
2. requires the input bytes already equal the canonical bytes;
3. requires the exact `$schemaId` for the entry point;
4. enforces the closed root/nested property sets from Task 2;
5. enforces System=`deployable=true` with four exact repos;
6. enforces Platform=`deployable=false` with the seven exact package IDs from the fixed values table;
7. requires one version and one Platform source SHA across all seven packages;
8. requires `authorSignedPackageSha256`, `publishedPackageSha256`, and `feedTransformation` on each package;
9. forbids a publication workflow final conclusion in `candidate-result.v2`; and
10. returns cloned canonical bytes and lowercase document SHA-256.

- [ ] **Step 6: Create exact positive and negative fixtures**

Use 64 `a` characters for non-secret SHA-256 fixture values and 40 `b` characters for Git SHAs. Positive Platform package IDs are the exact seven sorted IDs. Every mutation fixture starts from its named valid fixture and changes exactly one property; the missing/unknown/wrong-kind matrix above is mandatory in addition to the lane, missing-repository, and mixed-version negatives.

- [ ] **Step 7: Run tests and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~PrimaryCandidateValidationTests
```

Expected: PASS.

- [ ] **Step 8: Commit primary validation**

```powershell
git add -- src/CP6.Platform.Release tests/CP6.Platform.ReleaseTests/PrimaryCandidateValidationTests.cs contracts/release/v1/fixtures/primary
git diff --cached --check
git commit -m "feat(release): validate primary candidate lanes"
```

## Task 5: Implement trust, storage, evidence, lineage, and Locator rules

**Files:**

- Create: `src/CP6.Platform.Release/Cp6CandidateLocatorKeys.cs`
- Create: `src/CP6.Platform.Release/Cp6ReleaseValidationMode.cs`
- Create: `src/CP6.Platform.Release/Cp6PinnedTrustPolicy.cs`
- Create: `src/CP6.Platform.Release/Cp6SupportingContractValidator.cs`
- Create: `tests/CP6.Platform.ReleaseTests/TrustAndStorageValidationTests.cs`
- Create: `tests/CP6.Platform.ReleaseTests/SupportingContractValidationTests.cs`
- Create: `contracts/release/v1/fixtures/supporting/*.json`

- [ ] **Step 1: Write failing Locator/trust tests**

```csharp
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class TrustAndStorageValidationTests
{
    [Fact]
    public void Platform_tag_derives_fixed_locator_and_bundle_keys()
    {
        var keys = Cp6CandidateLocatorKeys.ForPlatformTag("v0.10.0-test.1");
        Assert.Equal("candidates/platform/v0.10.0-test.1/candidate-locator.v1.json", keys.LocatorKey);
        Assert.Equal("candidates/platform/v0.10.0-test.1/candidate-locator.v1.sigstore.json", keys.BundleKey);
    }

    [Theory]
    [InlineData("v01.10.0")]
    [InlineData("v0.10.0/escape")]
    [InlineData("v0.10.0..x")]
    [InlineData(" V0.10.0")]
    public void Unsafe_or_noncanonical_tags_are_rejected(string tag)
    {
        Assert.Equal("release-tag", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6CandidateLocatorKeys.ForPlatformTag(tag)).Code);
    }

    [Fact]
    public void Current_mode_rejects_revoked_and_policy_downgrade_while_audit_reports_history()
    {
        var policy = Cp6PinnedTrustPolicy.Parse(ReleaseTestData.Fixture("supporting", "trust.revoked.valid.json"));
        var signedAt = DateTimeOffset.Parse("2026-07-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture);
        var evaluatedAt = DateTimeOffset.Parse("2026-09-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("trust-revoked", Assert.Throws<Cp6ReleaseContractException>(() =>
            policy.RequireKey("sha256:" + new string('a', 64), "candidate-locator", 3, signedAt, evaluatedAt, Cp6ReleaseValidationMode.Current)).Code);
        var historical = policy.EvaluateHistoricalKey("sha256:" + new string('a', 64), "candidate-locator", 3, signedAt, evaluatedAt);
        Assert.True(historical.WasValidAtSigning);
        Assert.True(historical.CurrentlyRevoked);
        Assert.Equal("trust-policy-downgrade", Assert.Throws<Cp6ReleaseContractException>(() =>
            policy.RequireKey("sha256:" + new string('b', 64), "candidate-locator", 1, signedAt, evaluatedAt, Cp6ReleaseValidationMode.Current)).Code);
    }
}
```

- [ ] **Step 2: Write failing supporting-contract tests**

Cover these exact cases:

- object reference accepts only `storageAuthority=cp6-release-r2-v1`, the Task 2 content-addressed key grammar, a Task 2 media type, lowercase hash, and byte length 1..4 MiB; the key's two-character directory and 64-character directory must equal the reference hash prefix and full hash;
- evidence has at least one subject and binds producer workflow plus policy version;
- build provenance has one invocation ID, seven sorted final packages, and seven mapped pre-sign outputs;
- bootstrap evidence is required for `lineageMode=Bootstrap` and forbidden for successor lineage;
- transport is `testOnly=true`, not expired at the supplied evaluation time, and tied to the exact source run;
- a Locator `createdAtUtc` equals the referenced subject creation time; and
- `RequiredPublic` evidence is present for every normal acceptance conclusion.

For each supporting root below, add one canonical positive plus `-missing.invalid.json`, `-unknown.invalid.json`, and `-wrong-kind.invalid.json` single mutations. Put the unknown property in the deepest required nested object so nested closure is exercised, while the other two mutate a required root property. Assert stable codes `missing-property`, `unknown-property`, and `property-kind` through the named validator:

| Fixture stem | Validator |
|---|---|
| `release-gate` | `ValidateReleaseGateResult` |
| `lineage-bootstrap` | `ValidateSystemLineageBootstrapEvidence` |
| `evidence` | `ValidateEvidenceRecord` |
| `build-provenance` | `ValidateBuildInvocationProvenance` |
| `transport` | `ValidateTestPackageTransport` with fixed evaluation UTC `2026-09-01T00:00:00.000Z` |
| `trust` | `Cp6PinnedTrustPolicy.Parse` |

- [ ] **Step 3: Run tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter "FullyQualifiedName~TrustAndStorageValidationTests|FullyQualifiedName~SupportingContractValidationTests"
```

Expected: FAIL because the trust and supporting validators do not exist.

- [ ] **Step 4: Implement exact Locator key derivation**

```csharp
using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

public sealed record Cp6CandidateLocatorKeys(string LocatorKey, string BundleKey)
{
    private static readonly Regex Tag = new(
        "^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$",
        RegexOptions.CultureInvariant);

    public static Cp6CandidateLocatorKeys ForPlatformTag(string releaseTag)
    {
        if (!Tag.IsMatch(releaseTag) || releaseTag.Contains("..", StringComparison.Ordinal))
            throw new Cp6ReleaseContractException("release-tag", "Release tag is not canonical or path-safe.");
        var prefix = $"candidates/platform/{releaseTag}";
        return new($"{prefix}/candidate-locator.v1.json", $"{prefix}/candidate-locator.v1.sigstore.json");
    }
}
```

- [ ] **Step 5: Implement trust and supporting validators**

`Cp6PinnedTrustPolicy.Parse` accepts only canonical `pinned-trust-store.v1` bytes. It stores the current/minimum versions, explicit historical versions, fixed storage authority mapping, and keys indexed by key ID. `RequireKey(keyId, purpose, policyVersion, signedAtUtc, evaluationUtc, mode)` selects only an already-pinned key, checks purpose, policy version, the signing-time validity interval, and current revocation at `evaluationUtc`. `EvaluateHistoricalKey` receives the same identity and timestamps and returns `WasValidAtSigning` separately from `CurrentlyRevoked`. It never accepts a public key, endpoint, bucket, or policy supplied only by an unverified Locator.

`Cp6SupportingContractValidator` exposes these exact entry points:

```csharp
public static Cp6ValidatedReleaseDocument ValidateReleaseGateResult(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidateSystemLineageBootstrapEvidence(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidateEvidenceRecord(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidateBuildInvocationProvenance(ReadOnlySpan<byte> utf8Json);
public static Cp6ValidatedReleaseDocument ValidateTestPackageTransport(ReadOnlySpan<byte> utf8Json, DateTimeOffset evaluationUtc);
public static void RequireSystemLineage(ReadOnlySpan<byte> systemManifestUtf8, ReadOnlySpan<byte> bootstrapEvidenceUtf8);
public static void RequireRequiredPublicEvidence(IReadOnlyList<Cp6ValidatedReleaseDocument> evidence, IReadOnlyList<string> acceptedSubjectHashes);
```

Each method requires canonical input bytes and the exact schema ID before applying semantic checks not expressible in JSON Schema: ordinal set ordering, subject binding, seven-output provenance mapping, bootstrap/successor consistency, access-class sufficiency, and exact workflow/artifact binding. `RequireSystemLineage` receives an empty bootstrap span for successor lineage and rejects a nonempty bootstrap object; bootstrap lineage requires a nonempty, valid, correctly bound object.

- [ ] **Step 6: Create one positive and one single-mutation negative fixture per rule**

Fixtures include:

```text
release-gate.valid.json
release-gate-unbound.invalid.json
evidence.valid.json
evidence-authority.invalid.json
evidence-unbound.invalid.json
build-provenance.valid.json
build-provenance-mixed-invocation.invalid.json
lineage-bootstrap.valid.json
lineage-bootstrap-unsigned.invalid.json
transport.valid.json
transport-expired.invalid.json
trust.valid.json
trust.revoked.valid.json
trust-downgrade.invalid.json
```

Add the 18 structural mutation fixtures using the exact six stems and suffixes from Step 2. Semantic negatives in the list above change only the named invariant. In `trust.revoked.valid.json`, key `sha256:` plus 64 `a` characters is valid from `2026-01-01T00:00:00.000Z` through `2027-01-01T00:00:00.000Z` and revoked at `2026-08-01T00:00:00.000Z`, which makes the fixed July signing time historically valid and the September evaluation currently revoked.

- [ ] **Step 7: Run tests and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter "FullyQualifiedName~TrustAndStorageValidationTests|FullyQualifiedName~SupportingContractValidationTests"
```

Expected: PASS.

- [ ] **Step 8: Commit supporting validation**

```powershell
git add -- src/CP6.Platform.Release tests/CP6.Platform.ReleaseTests contracts/release/v1/fixtures/supporting
git diff --cached --check
git commit -m "feat(release): enforce trust and evidence contracts"
```

## Task 6: Verify package contents and the exact seven-package test set

**Files:**

- Create: `eng/p10/New-P10TestCertificate.ps1`
- Create: `eng/p10/Pack-P10TestPackages.ps1`
- Create: `eng/p10/New-P10TestPackageSet.ps1`
- Create: `eng/p10/Test-P10TestPackageSet.ps1`
- Create: `eng/p10/New-P10TransportRecord.ps1`
- Create: `tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj`
- Create: `tools/CP6.Platform.ReleaseTool/Program.cs`
- Create: `tests/CP6.Platform.ReleaseTests/P10PackageTests.cs`
- Create: `tests/CP6.Platform.ReleaseTests/P10PackageTestHarness.cs`
- Create: `tests/p10/test-package-scripts.Tests.ps1`
- Modify: `CP6.Platform.sln`

- [ ] **Step 1: Write failing package and script-contract tests**

`P10PackageTests` must assert:

```csharp
[Fact]
public void Release_package_contains_only_dll_xml_readme_and_release_contract_assets()
{
    var entries = P10PackageTestHarness.PackReleasePackage("0.10.0-test.local.1");
    Assert.Contains("lib/net8.0/CP6.Platform.Release.dll", entries);
    Assert.All(entries, name => Assert.True(
        name is "lib/net8.0/CP6.Platform.Release.dll" or "lib/net8.0/CP6.Platform.Release.xml" or "README.md" or "[Content_Types].xml" or "CP6.Platform.Release.nuspec" ||
        name.StartsWith("contracts/release/v1/", StringComparison.Ordinal) ||
        name.StartsWith("_rels/", StringComparison.Ordinal) ||
        name.StartsWith("package/", StringComparison.Ordinal)));
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
    Assert.All(packages, package => Assert.Equal("CN=CP6 Platform P10 TEST ONLY", package.GetProperty("certificateSubject").GetString()));
}
```

`P10PackageTestHarness.PackReleasePackage` creates a GUID-named directory under `artifacts/p10-test/unit/`, runs `dotnet pack src/CP6.Platform.Release/CP6.Platform.Release.csproj --configuration Release --no-restore -p:PackageVersion=<version> -p:IncludeSymbols=false --output <directory>`, returns the `ZipArchive` entry names, and removes the directory in `finally`. `BuildTestSetForCurrentCommit` creates a second GUID-named directory under the same unit root, runs `New-P10TestPackageSet.ps1` with the exact current 40-character lowercase commit SHA, run ID `1`, attempt `1`, calculates lowercase SHA-256 over `test-signing-public.cer`, runs `Test-P10TestPackageSet.ps1` with that fingerprint and the same source/run tuple, parses `test-package-manifest.v1.json` into an in-memory `JsonDocument`, removes the directory, then returns the parsed document. Both helpers use `ReleaseTestData.RepositoryRoot`, `ProcessStartInfo.ArgumentList`, a 10-minute timeout, captured standard output/error, and throw an `InvalidOperationException` containing exit code and captured output on any nonzero exit.

The Pester script asserts all five PowerShell scripts use explicit parameters, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`, paths under `artifacts/p10-test/`, no `nuget push`, no formal feed URL, no R2 command, no secret persistence, and no `--skip-duplicate`.

- [ ] **Step 2: Run focused tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~P10PackageTests
pwsh -NoProfile -File tests/p10/test-package-scripts.Tests.ps1
```

Expected: FAIL because the dedicated P10 scripts, CLI tool, and seven-package output do not exist.

- [ ] **Step 3: Add a P10-only exact pack list without changing the P08 pack contract**

Keep `eng/pack-release.ps1`, its `0.8.0-alpha.2` default, and its five-package P08 list unchanged because architecture/unit tests and the existing Contract gate treat those values as historical public behavior. Put the P10 list only in `Pack-P10TestPackages.ps1`:

```powershell
param(
    [Parameter(Mandatory)][ValidatePattern('^0\.10\.0-test\.[0-9a-f]{12}\.[1-9][0-9]*$')][string]$PackageVersion,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceGitSha,
    [Parameter(Mandatory)][string]$OutputPath
)

$projects = @(
    'src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj',
    'src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj',
    'src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj',
    'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj',
    'src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj',
    'src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj',
    'src/CP6.Platform.Release/CP6.Platform.Release.csproj'
)
```

Resolve `OutputPath` and reject it unless it is below `<repo>/artifacts/p10-test/`. Require an existing Release solution build, then run `dotnet pack <project> --configuration Release --no-build --no-restore -p:PackageVersion=$PackageVersion -p:RepositoryCommit=$SourceGitSha -p:ContinuousIntegrationBuild=true -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg --output $OutputPath` once per listed project. Reject duplicate package IDs and require exactly 7 `.nupkg` plus 7 `.snupkg` files.

- [ ] **Step 4: Add the repository-local release contract CLI**

Add the tool to the solution under solution folder `tools`:

```powershell
dotnet sln CP6.Platform.sln add tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj --solution-folder tools
```

The tool targets `net8.0`, enables nullable/implicit usings, references `CP6.Platform.Release`, and exposes exactly these commands:

```text
CP6.Platform.ReleaseTool canonicalize <input-json> <output-json>
CP6.Platform.ReleaseTool validate-build-provenance <input-json>
CP6.Platform.ReleaseTool validate-evidence <input-json>
CP6.Platform.ReleaseTool validate-transport <input-json> <evaluation-utc>
```

`canonicalize` writes `Cp6DeterministicJson.Canonicalize(File.ReadAllBytes(input))` with `File.WriteAllBytes`; validation commands read exact bytes and call the matching `Cp6SupportingContractValidator` method. Accept `evaluation-utc` only as invariant round-trip UTC (`O` format ending in `Z`). Return `0` on success, `2` after writing only `<exception.Code>: <exception.Message>` to stderr for `Cp6ReleaseContractException`, `64` for an unknown command or wrong argument count, and `1` with only `release-tool-internal-error` for any other exception. Do not print document contents, environment values, passwords, exception stack traces, or paths.

- [ ] **Step 5: Implement ephemeral test certificate creation**

`New-P10TestCertificate.ps1` rejects non-Windows hosts, captures one `DateTimeOffset.UtcNow`, then uses `System.Security.Cryptography.X509Certificates.CertificateRequest` with RSA-2048, SHA-256, subject `CN=CP6 Platform P10 TEST ONLY`, Basic Constraints `CA=false`, Key Usage `DigitalSignature`, code-signing EKU `1.3.6.1.5.5.7.3.3`, Subject Key Identifier, `notBefore=now-5 minutes`, and `notAfter=now+91 days` so S03 can verify during the 90-day artifact lifetime. It exports a PFX protected by a 32-byte cryptographically random base64 password held only in memory and exports the public certificate as `test-signing-public.cer`; it never imports either certificate into an operating-system store. It returns PFX/CER paths, password, and the lowercase SHA-256 fingerprint. The caller deletes the PFX and clears password state in `finally`; only the public CER and fingerprint enter artifacts. Tests inject a failure after signing and require the same cleanup.

- [ ] **Step 6: Implement the one-build package-set generator**

`New-P10TestPackageSet.ps1` requires:

```powershell
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceGitSha,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$RunId,
    [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$RunAttempt,
    [string]$OutputPath = 'artifacts/p10-test/packages'
)
```

It derives version `0.10.0-test.<first12sha>.<attempt>` and invocation ID `p10-s02:<sha>:<runId>:<attempt>`, verifies `git rev-parse HEAD` equals `SourceGitSha`, restores once, and performs one solution Release build with `-p:PackageVersion=<derived-version> -p:Version=<derived-version> -p:RepositoryCommit=<source-sha> -p:ContinuousIntegrationBuild=true`. Against those exact outputs it runs ArchitectureTests, UnitTests, and ReleaseTests with `--no-build`; the ReleaseTests command uses `--filter FullyQualifiedName!~P10PackageTests` to prevent recursive package generation. Each test command writes a TRX into a private temporary results directory; parse only `ResultSummary/Counters` into a canonical gate summary containing gate name, source SHA, invocation ID, start/end UTC, total, executed, passed, failed, error, timeout, aborted, and conclusion. Delete the raw TRX because it may contain machine paths. Only after all gates pass, call `Pack-P10TestPackages.ps1` once with the derived version and source SHA and require exactly 7 `.nupkg` plus 7 `.snupkg` files.

For each package it:

1. records the pre-sign SHA-256;
2. signs with the ephemeral test PFX and `dotnet nuget sign --hash-algorithm SHA256 --overwrite` without a production TSA;
3. runs `CP6.Platform.ReleaseTool verify-test-package <path> <lowercase-test-fingerprint>`, which composes NuGet's integrity, CMS trust/validity, and exact-author-fingerprint allow-list providers while allowing an untrusted root only for that fingerprint;
4. records final file SHA-256, ID, version, source SHA, invocation ID, certificate subject/fingerprint, `testOnly=true`, and `timestampPolicy=TestOnlyNone`; and
5. rejects paths, credentials, formal feed URLs, production signer IDs, and mixed versions.

Create `test-package-manifest.v1.json` as a tooling manifest, not an eleventh public release contract. It is a closed canonical object with exactly:

- `profile=cp6-p10-test-package-set-v1` and `testOnly=true`;
- `platformSourceSha`, `sourceRunId`, `sourceRunAttempt`, `buildInvocationId`, and `packageVersion`;
- `certificateSubject`, `certificateFingerprint`, and `timestampPolicy=TestOnlyNone`;
- `lockedRestore` containing `mode=locked`, `sourceMappingPattern=CP6.Platform.*`, the seven sorted package IDs, and the one exact version; and
- seven sorted `packages` entries containing package ID/version/source SHA, ordinary/symbol filenames, both final SHA-256 values, certificate subject/fingerprint, and `testOnly=true`.

Write raw `test-package-manifest.v1.json`, `build-invocation-provenance.v1.json`, and `test-only-evidence-record.v1.json` to a private temporary directory. Invoke `CP6.Platform.ReleaseTool canonicalize` for all three final files, then invoke `validate-build-provenance` and `validate-evidence` for the two public-contract documents. Copy sanitized gate summaries into `artifacts/p10-test/packages/evidence/gates/`, write canonical `sha256.json` covering every artifact file except itself, delete the raw files, PFX, password variables, and private key in `finally`, and leave only packages, public CER, canonical manifest/evidence, hashes, locked-restore metadata, and sanitized gate summaries.

- [ ] **Step 7: Implement independent package-set verification**

`Test-P10TestPackageSet.ps1` accepts `PackagePath`, `ExpectedSourceGitSha`, `ExpectedRunId`, `ExpectedRunAttempt`, and mandatory lowercase `ExpectedCertificateFingerprint`. It rejects non-Windows hosts, derives the expected version/invocation ID, calculates the fingerprint of the single `test-signing-public.cer`, and requires it to equal the expected value. Without importing any certificate, it calls the Release tool's isolated NuGet verifier for every package and exact fingerprint. It fails unless all 14 package files, the tooling manifest and locked-restore fields, provenance, evidence, hashes, signatures, package metadata, gate summaries, and test-only markers match exactly. It must reject any formal signer, production timestamp claim, `CP6.Platform.Testing` package, missing Release/Deployment package, second certificate, certificate subject other than `CN=CP6 Platform P10 TEST ONLY`, or final bytes differing from `sha256.json`.

- [ ] **Step 8: Implement transport record creation**

`New-P10TransportRecord.ps1` runs only after package artifact upload and requires exact workflow identity plus package artifact ID, `sha256:<digest>`, API creation time, and API expiry time. It emits canonical `test-package-transport.v1.json`; it does not include its own artifact ID/digest.

- [ ] **Step 9: Run package tests and script tests GREEN**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~P10PackageTests
pwsh -NoProfile -File tests/p10/test-package-scripts.Tests.ps1
```

Expected: PASS; temporary certificate private material is absent after both success and injected failure.

- [ ] **Step 10: Commit package-set tooling**

```powershell
git add -- CP6.Platform.sln eng/p10 tools/CP6.Platform.ReleaseTool tests/CP6.Platform.ReleaseTests/P10PackageTests.cs tests/CP6.Platform.ReleaseTests/P10PackageTestHarness.cs tests/p10
git diff --cached --check
git commit -m "build(release): create seven-package test candidate"
```

## Task 7: Add the S02 immutable artifact workflow and CI gates

**Files:**

- Create: `.github/workflows/p10-test-candidate.yml`
- Create: `tests/CP6.Platform.ReleaseTests/P10WorkflowContractTests.cs`
- Modify: `eng/verify.ps1`
- Modify: `TESTING.md`

- [ ] **Step 1: Write failing workflow-contract tests**

Create tests that parse workflow text and require:

- `workflow_dispatch` with mandatory `expected_commit`;
- `runs-on: windows-latest` for the self-issued test-root lifecycle;
- dispatch only from `main`, `inputs.expected_commit` equal to the event-frozen `github.sha`, and checkout that exact event SHA;
- `contents: read`, `actions: read`, and no `packages: write`, `id-token: write`, or environment secret;
- pinned checkout/setup-dotnet/upload-artifact action SHAs;
- first artifact name `p10-s02-packages-${{ inputs.expected_commit }}-${{ github.run_attempt }}`;
- second artifact name `p10-s02-transport-${{ inputs.expected_commit }}-${{ github.run_attempt }}`;
- `overwrite` absent or false and `retention-days: 90` on both uploads;
- package upload output ID/digest consumed by the transport-record step;
- GitHub API metadata queried by exact artifact ID before transport creation;
- independent package-set verification before upload; and
- no NuGet push, GitHub Packages URL, R2 command, cosign key, deployment command, or formal state claim.
- the existing `platform-validation.yml` matrix still runs the Contract gate on both `ubuntu-latest` and `windows-latest`, which is the cross-platform path for deterministic release-contract tests.

- [ ] **Step 2: Run workflow tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~P10WorkflowContractTests
```

Expected: FAIL because the workflow is missing.

- [ ] **Step 3: Create the test-candidate workflow**

Use this job skeleton with pinned action SHAs already approved in the repository:

```yaml
name: p10-test-candidate

on:
  workflow_dispatch:
    inputs:
      expected_commit:
        description: Exact CP6.Platform main commit approved for the P10 test candidate
        required: true
        type: string

permissions:
  contents: read
  actions: read

jobs:
  build-test-candidate:
    runs-on: windows-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683
        with:
          ref: ${{ github.sha }}
          persist-credentials: false
          fetch-depth: 0
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
        with:
          dotnet-version: 8.0.x
      - name: Assert exact main source
        shell: pwsh
        run: |
          if ('${{ github.ref }}' -cne 'refs/heads/main') { throw 'Dispatch must target main.' }
          if ('${{ github.sha }}' -cne '${{ inputs.expected_commit }}') { throw 'Expected commit differs from the dispatch event main SHA.' }
          if ((git rev-parse HEAD).Trim() -cne '${{ github.sha }}') { throw 'Checkout mismatch.' }
      - name: Build and verify test package set
        shell: pwsh
        run: |
          ./eng/p10/New-P10TestPackageSet.ps1 -SourceGitSha '${{ inputs.expected_commit }}' -RunId ${{ github.run_id }} -RunAttempt ${{ github.run_attempt }}
          $cerBytes = [IO.File]::ReadAllBytes((Resolve-Path artifacts/p10-test/packages/test-signing-public.cer))
          $testFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cerBytes)).ToLowerInvariant()
          ./eng/p10/Test-P10TestPackageSet.ps1 -PackagePath artifacts/p10-test/packages -ExpectedSourceGitSha '${{ inputs.expected_commit }}' -ExpectedRunId ${{ github.run_id }} -ExpectedRunAttempt ${{ github.run_attempt }} -ExpectedCertificateFingerprint $testFingerprint
      - name: Reject private material and secret-shaped text
        shell: pwsh
        run: |
          $files = @(Get-ChildItem artifacts/p10-test/packages -Recurse -File)
          $private = @($files | Where-Object { $_.Extension -in '.pfx', '.p12', '.pem', '.key' -or $_.Name -match '(?i)password|private[-_]?key' })
          if ($private.Count -ne 0) { throw "Private material found: $($private.Name -join ', ')" }
          $text = @($files | Where-Object { $_.Extension -in '.json', '.txt', '.md' })
          $matches = @($text | Select-String -Pattern '-----BEGIN (?:RSA |EC )?PRIVATE KEY-----|(?i)(?:password|token|secret)\s*[:=]\s*[^\s]+' )
          if ($matches.Count -ne 0) { throw 'Secret-shaped text found in package artifact.' }
      - name: Upload immutable package set
        id: packages
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02
        with:
          name: p10-s02-packages-${{ inputs.expected_commit }}-${{ github.run_attempt }}
          path: artifacts/p10-test/packages/**
          if-no-files-found: error
          overwrite: false
          retention-days: 90
      - name: Query package artifact metadata and create transport record
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $artifact = gh api repos/${{ github.repository }}/actions/artifacts/${{ steps.packages.outputs.artifact-id }} | ConvertFrom-Json
          $artifactDigest = '${{ steps.packages.outputs.artifact-digest }}'
          if ($artifactDigest -cmatch '^[0-9a-f]{64}$') { $artifactDigest = "sha256:$artifactDigest" }
          if ($artifactDigest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw 'Package artifact digest is not canonical SHA-256.' }
          if (([string]$artifact.digest) -and ([string]$artifact.digest) -cne $artifactDigest) { throw 'Upload output digest differs from the artifact API digest.' }
          ./eng/p10/New-P10TransportRecord.ps1 -OutputPath artifacts/p10-test/transport/test-package-transport.v1.json -PlatformSourceSha '${{ inputs.expected_commit }}' -RunId ${{ github.run_id }} -RunAttempt ${{ github.run_attempt }} -PackageArtifactId $artifact.id -PackageArtifactDigest $artifactDigest -CreatedAtUtc $artifact.created_at -ExpiresAtUtc $artifact.expires_at
      - name: Validate transport and reject secret-shaped text
        shell: pwsh
        run: |
          dotnet run --project tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj --configuration Release --no-build -- validate-transport artifacts/p10-test/transport/test-package-transport.v1.json ([DateTimeOffset]::UtcNow.ToString('O'))
          $files = @(Get-ChildItem artifacts/p10-test/transport -Recurse -File)
          if (@($files | Where-Object { $_.Extension -in '.pfx', '.p12', '.pem', '.key' -or $_.Name -match '(?i)password|private[-_]?key' }).Count -ne 0) { throw 'Private material found in transport artifact.' }
          if (@($files | Select-String -Pattern '-----BEGIN (?:RSA |EC )?PRIVATE KEY-----|(?i)(?:password|token|secret)\s*[:=]\s*[^\s]+').Count -ne 0) { throw 'Secret-shaped text found in transport artifact.' }
      - name: Upload immutable transport record
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02
        with:
          name: p10-s02-transport-${{ inputs.expected_commit }}-${{ github.run_attempt }}
          path: artifacts/p10-test/transport/test-package-transport.v1.json
          if-no-files-found: error
          overwrite: false
          retention-days: 90
```

Gate summaries are already under `packages/evidence/gates/`, so they are uploaded only as part of the package artifact.

- [ ] **Step 4: Wire Release tests into common gates**

Add `CP6.Platform.ReleaseTests` to the Contract gate after Architecture and before reproducible package assertions:

```powershell
Invoke-DotNetStep -Name 'ReleaseContracts' -Arguments @(
    'test', 'tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj',
    '--configuration', 'Release', '--no-build'
)
```

Immediately after the Release test command, run `tests/p10/test-package-scripts.Tests.ps1`, fail on a nonzero exit, and append a `P10PackageScriptContracts` Passed check to the gate summary. Do not add a duplicate validation step to `platform-validation.yml`: its existing Windows/Linux matrix already invokes the Contract gate. Do not add the S02 workflow to PR execution; it remains an exact-main manual dispatch.

- [ ] **Step 5: Run workflow and gate tests GREEN**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~P10WorkflowContractTests
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
```

Expected: PASS and machine-readable Contract evidence includes `ReleaseContracts`.

- [ ] **Step 6: Update testing documentation**

Add a P10 row to `TESTING.md` describing Release tests and the exact-main S02 workflow. State that S02 uses test-only signing, never pushes packages, and expires after 90 days.

- [ ] **Step 7: Commit workflow and gates**

```powershell
git add -- .github/workflows/p10-test-candidate.yml eng/verify.ps1 tests/CP6.Platform.ReleaseTests/P10WorkflowContractTests.cs TESTING.md
git diff --cached --check
git commit -m "ci(release): preserve P10 test candidate artifacts"
```

## Task 8: Complete Platform documentation and repository-level verification

**Files:**

- Create: `docs/P10-RELEASE-GOVERNANCE.md`
- Modify: `README.md`
- Modify: `VERSION`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Write the failing documentation architecture test**

Add `P10_Documentation_IsCompleteAndNonDeployable` requiring:

- `VERSION` equals `0.10.0.0`;
- README links `docs/P10-RELEASE-GOVERNANCE.md`;
- P10 docs list all seven package IDs and four primary/supporting contract groups;
- docs contain `Implemented / Test Candidate`, `testOnly=true`, `deployable=false`, `GitHub Packages`, `S03`, and artifact retention;
- docs explicitly deny formal package publication, System candidate publication, Portal fabrication, R2 Locator publication, and deployment; and
- no docs claim `VersionId`, Object Lock, `Frozen / Consumable`, or a real certificate exists.

- [ ] **Step 2: Run the documentation test RED**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P10_Documentation_IsCompleteAndNonDeployable
```

Expected: FAIL because P10 docs and repository version are missing.

- [ ] **Step 3: Write the P10 Platform operator/developer document**

Document:

- package ownership and exact seven-package set;
- primary and supporting schema names;
- deterministic byte profile and limits;
- System versus Platform candidate lane separation;
- test certificate and artifact transport boundaries;
- exact local test commands;
- S02 dispatch and artifact verification procedure;
- status ceiling and missing S03-S06 evidence; and
- external formal inputs that remain unavailable.

Update `README.md` with the P10 link and `VERSION` to `0.10.0.0`.

- [ ] **Step 4: Run the focused documentation test GREEN**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P10_Documentation_IsCompleteAndNonDeployable
```

Expected: PASS.

- [ ] **Step 5: Run the complete risk-proportionate matrix**

```powershell
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Format -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Build -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Integration -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate E2E -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Security -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -P09Contract -Profile ci
dotnet test CP6.Platform.sln --configuration Release --no-build
```

Expected: every applicable gate passes. Performance and Migration remain explicit `NotApplicable`; do not convert them into silent skips.

- [ ] **Step 6: Review the complete implementation diff**

```powershell
git status --short
git diff --check
git diff origin/main...HEAD --stat
git diff origin/main...HEAD -- . ':(exclude)docs/superpowers/specs/2026-09-01-p10-release-governance-design.md' ':(exclude)docs/superpowers/plans/2026-09-01-p10-s00-s02-release-contracts.md'
git grep -n -I -E '(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|client_secret|access_token|password\s*[:=])' -- src tests contracts eng .github docs
```

Expected: only P10 S00-S02 files and intentional shared solution/gate changes; no private key, token, machine path, formal package publication, R2 write, or runtime feature.

- [ ] **Step 7: Commit documentation and status**

```powershell
git add -- docs/P10-RELEASE-GOVERNANCE.md README.md VERSION tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git diff --cached --check
git commit -m "docs(release): record P10 test candidate boundary"
```

## Task 9: Merge S00-S01 and dispatch the exact-main S02 test candidate

**Files:**

- No source edits before merge
- Workflow outputs under GitHub Actions
- Evidence update after real run: `docs/P10-RELEASE-GOVERNANCE.md`

- [ ] **Step 1: Push and open the implementation PR**

```powershell
git push -u origin codex/p10-s00-s02-release-contracts
$implementationPrUrl = gh pr create --repo GTX537/CP6.Platform --base main --head codex/p10-s00-s02-release-contracts --title "feat: add P10 release contracts and test transport" --body "Implements P10 S00-S01 and prepares S02: independent Release package, deterministic JSON, primary/supporting schemas, strict lane/trust/evidence validation, seven-package test-only tooling, and immutable artifact workflow. It does not publish packages, R2 candidates, or deploy anything."
$implementationPrNumber = gh pr view $implementationPrUrl --repo GTX537/CP6.Platform --json number --jq .number
gh pr checks $implementationPrNumber --repo GTX537/CP6.Platform --watch
```

Expected: Windows/Linux matrix and all required jobs pass.

- [ ] **Step 2: Merge and capture exact S01 main**

```powershell
gh pr merge $implementationPrNumber --repo GTX537/CP6.Platform --merge
git fetch origin main --prune
$s01MainSha = (git rev-parse origin/main).Trim()
gh pr view $implementationPrNumber --repo GTX537/CP6.Platform --json state,mergeCommit --jq '{state:.state,sha:.mergeCommit.oid}'
```

Expected: merged commit is contained in exact current main.

- [ ] **Step 3: Dispatch S02 against exact main**

```powershell
$dispatchStartedUtc = [DateTimeOffset]::UtcNow
gh workflow run p10-test-candidate.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$s01MainSha
Start-Sleep -Seconds 3
$runs = @(gh run list --repo GTX537/CP6.Platform --workflow p10-test-candidate.yml --branch main --event workflow_dispatch --limit 20 --json databaseId,headSha,createdAt,status | ConvertFrom-Json | Where-Object { $_.headSha -ceq $s01MainSha -and [DateTimeOffset]$_.createdAt -ge $dispatchStartedUtc.AddSeconds(-5) })
if ($runs.Count -ne 1) { throw "Expected exactly one newly dispatched run for $s01MainSha; found $($runs.Count)." }
$runId = $runs[0].databaseId
gh run watch $runId --repo GTX537/CP6.Platform --exit-status
```

Expected: run succeeds and `headSha` equals `$s01MainSha`.

- [ ] **Step 4: Independently inspect both artifacts**

```powershell
$artifacts = gh api repos/GTX537/CP6.Platform/actions/runs/$runId/artifacts | ConvertFrom-Json
$run = gh api repos/GTX537/CP6.Platform/actions/runs/$runId | ConvertFrom-Json
$packageArtifact = $artifacts.artifacts | Where-Object name -like 'p10-s02-packages-*'
$transportArtifact = $artifacts.artifacts | Where-Object name -like 'p10-s02-transport-*'
if (@($packageArtifact).Count -ne 1 -or @($transportArtifact).Count -ne 1) { throw 'Expected one package and one transport artifact.' }
if ($packageArtifact.expired -or $transportArtifact.expired) { throw 'S02 artifact is already expired.' }
gh run download $runId --repo GTX537/CP6.Platform --name $packageArtifact.name --dir artifacts/p10-s02-audit/packages
gh run download $runId --repo GTX537/CP6.Platform --name $transportArtifact.name --dir artifacts/p10-s02-audit/transport
$cerBytes = [IO.File]::ReadAllBytes((Resolve-Path artifacts/p10-s02-audit/packages/test-signing-public.cer))
$testFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cerBytes)).ToLowerInvariant()
pwsh -NoProfile -File ./eng/p10/Test-P10TestPackageSet.ps1 -PackagePath artifacts/p10-s02-audit/packages -ExpectedSourceGitSha $s01MainSha -ExpectedRunId $runId -ExpectedRunAttempt $run.run_attempt -ExpectedCertificateFingerprint $testFingerprint
dotnet run --project tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj --configuration Release --no-build -- validate-transport artifacts/p10-s02-audit/transport/test-package-transport.v1.json ([DateTimeOffset]::UtcNow.ToString('O'))
$transport = Get-Content artifacts/p10-s02-audit/transport/test-package-transport.v1.json -Raw | ConvertFrom-Json
if ([long]$transport.packageArtifactId -ne [long]$packageArtifact.id -or $transport.packageArtifactDigest -cne $packageArtifact.digest -or [long]$transport.sourceRunId -ne [long]$runId -or [int]$transport.sourceRunAttempt -ne [int]$run.run_attempt) { throw 'Transport record does not bind the downloaded package artifact and source run.' }
```

Expected: exactly 14 packages and all hashes, signatures, source/run identities, test-only markers, and transport fields verify.

- [ ] **Step 5: Create a separate evidence branch from current main**

Run from `D:\CP6\CP6.Platform`:

```powershell
git fetch origin main --prune
$evidenceWorktree = 'D:\CP6.Platform-worktrees\p10-s02-test-evidence'
git worktree add -b codex/p10-s02-test-candidate-evidence $evidenceWorktree origin/main
```

Update `docs/P10-RELEASE-GOVERNANCE.md` with exact S01 main SHA, workflow run/attempt, both artifact IDs/names/digests/expiry times, package version, public test certificate fingerprint, and verification result. Keep status `Implemented / Test Candidate`.

- [ ] **Step 6: Commit and merge S02 evidence**

```powershell
git add -- docs/P10-RELEASE-GOVERNANCE.md
git diff --cached --check
git commit -m "docs(release): record P10 S02 test candidate"
git push -u origin codex/p10-s02-test-candidate-evidence
$evidencePrUrl = gh pr create --repo GTX537/CP6.Platform --base main --head codex/p10-s02-test-candidate-evidence --title "docs: record P10 S02 test candidate" --body "Records immutable test-only artifact identities from the exact-main P10 S02 run. It does not promote packages, claim formal signing, or mark P10 consumable."
$evidencePrNumber = gh pr view $evidencePrUrl --repo GTX537/CP6.Platform --json number --jq .number
gh pr checks $evidencePrNumber --repo GTX537/CP6.Platform --watch
gh pr merge $evidencePrNumber --repo GTX537/CP6.Platform --merge
git fetch origin main --prune
$s02MainSha = (git rev-parse origin/main).Trim()
```

Expected: evidence PR merged; remote main contains exact S02 evidence and remains `Implemented / Test Candidate`.

## Task 10: Produce the exact S03 handoff and stop at the repository boundary

**Files:**

- Read: `docs/P10-RELEASE-GOVERNANCE.md`
- Future CRM plan location: `CP6.CRM/docs/superpowers/plans/2026-09-01-p10-s03-crm-test-consumer.md`

- [ ] **Step 1: Record the handoff tuple**

The S03 planning input is exactly:

```text
platformMainSha
platformWorkflowPath
platformWorkflowFileSha
workflowRunId
workflowRunAttempt
packageArtifactId
packageArtifactDigest
transportArtifactId
transportArtifactDigest
artifactExpiryUtc
testPackageVersion
testCertificateFingerprint
```

Read every value from merged S02 evidence and GitHub API. Do not copy package files into CRM or infer an artifact by name alone.

- [ ] **Step 2: Verify no downstream action occurred**

```powershell
gh api repos/GTX537/CP6.Platform/actions/runs/$runId --jq '{head_sha:.head_sha,conclusion:.conclusion,run_attempt:.run_attempt}'
git show origin/main:docs/P10-RELEASE-GOVERNANCE.md | Select-String 'Implemented / Test Candidate'
git show origin/main:.github/workflows/p10-test-candidate.yml | Select-String -Pattern 'nuget\s+push|nuget\.pkg\.github\.com|aws\s+s3|rclone|cosign\s+sign|kubectl|docker\s+compose' -CaseSensitive:$false
```

Expected: successful exact run, documented test-only state, and no formal publication/R2/deployment command.

- [ ] **Step 3: End this plan**

Create the S03 CRM plan from the exact handoff tuple. Do not modify CRM, publish a formal package, configure a real certificate, or start S04 from the Platform implementation/evidence branches.

## Final S00-S02 completion checklist

- [ ] Design and plan merged before implementation branch creation.
- [ ] `CP6.Platform.Release` is the seventh independent packable package.
- [ ] Ten contract schemas plus shared definitions and assets manifest are closed and valid.
- [ ] Deterministic JSON golden bytes match on Windows and Linux.
- [ ] System and Platform candidate validators reject lane substitution.
- [ ] Trust downgrade, revocation, object authority, evidence binding, lineage, and transport rules fail closed.
- [ ] Test package set contains exactly seven IDs, one version, one source SHA, one invocation, and test-only signatures.
- [ ] Package and transport artifacts are separate, immutable v4 artifacts with 90-day retention.
- [ ] No formal feed, R2, GHCR, CRM, Portal, Azure release, or deployment mutation occurred.
- [ ] Exact S02 evidence is merged to Platform main with status `Implemented / Test Candidate`.
- [ ] S03 receives an exact identity tuple rather than package copies or mutable names.
