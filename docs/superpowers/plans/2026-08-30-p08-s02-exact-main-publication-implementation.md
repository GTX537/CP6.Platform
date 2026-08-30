# P08-S02 Exact-Main Immutable NuGet Publication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish exactly five immutable `0.8.0-alpha.1` CP6.Platform NuGet packages from one approved Platform `main` commit, preserve complete package and real-profile evidence, and close only P08-S02 with an auditable evidence PR.

**Architecture:** Keep `.github/workflows/publish-alpha.yml` as the sole package publisher and GitHub Packages as the sole registry. First harden its existing artifact upload with one repository architecture test so P05 and P06 real-profile evidence cannot be overwritten or omitted. Merge that correction, wait for all four main jobs, dispatch the publisher against that exact merge SHA, verify run/artifact/package identities independently, then record immutable facts on a separate evidence branch. Do not add a second feed, publisher, package version, runtime behavior, or deployment path.

**Tech Stack:** Git/Git worktrees, GitHub CLI, GitHub Actions, PowerShell 7, .NET 8, xUnit, NuGet/GitHub Packages, SHA-256.

---

## Execution boundary

This plan implements only **P08-S02**. The current planning worktree and task branch are:

```text
D:\CP6-worktrees\p08-s02-publication-design
codex/p08-s02-publication-design
```

The branch was created from verified Platform `main@33659d8dbf2e9339ccfeedee590139204dc4a029` and already contains the approved design commits. Continue on this branch for the evidence-retention implementation because the approved design requires the design, plan, architecture assertion, and workflow correction to land in one PR. Do not touch or clean the user's root worktree.

Publication is authorized only after that PR merges and the exact merge commit passes all four Platform main jobs. A publication run is valid only while remote Platform `main` remains that exact SHA through the terminal observation.

The only approved ordinary package files are:

```text
CP6.Platform.Contracts.0.8.0-alpha.1.nupkg
CP6.Platform.Abstractions.0.8.0-alpha.1.nupkg
CP6.Platform.AspNetCore.0.8.0-alpha.1.nupkg
CP6.Platform.Messaging.0.8.0-alpha.1.nupkg
CP6.Platform.EntityFramework.0.8.0-alpha.1.nupkg
```

Each must have one same-version `.snupkg`. Only the five ordinary packages are pushed. All ten package files, `sha256.json`, release/verify output, and both independent real-profile directories must be retained in one artifact.

Stop immediately if a package push is partial, Platform main drifts during publication, the artifact is incomplete, or identities/hashes disagree. Do not retry, delete, unlist, overwrite, use `--skip-duplicate`, or silently change the version. A forward version requires a new reviewed decision.

## Task 1: Lock complete publication evidence with a failing architecture test

**Files:**

- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`
- Test: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Add the publication workflow contract test**

Add this test near the other P08 architecture guards:

```csharp
[Fact]
public void P08_PublicationWorkflow_AlwaysPreservesCompleteEvidence()
{
    var workflow = File.ReadAllText(Path.Combine(
        RepositoryRoot,
        ".github",
        "workflows",
        "publish-alpha.yml"));

    foreach (var required in new[]
    {
        "if: always()",
        "name: p08-alpha-${{ inputs.expected_commit }}",
        "artifacts/release/**",
        "artifacts/verify/**",
        "artifacts/p05-integration/**",
        "artifacts/p06-sql-integration/**",
        "if-no-files-found: error",
        "retention-days: 30"
    })
    {
        Assert.Contains(required, workflow, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

```powershell
$env:PATH = "C:\Users\tt\.dotnet;$env:PATH"
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~P08_PublicationWorkflow_AlwaysPreservesCompleteEvidence"
```

Expected: FAIL because the workflow does not yet contain `artifacts/p05-integration/**` and `artifacts/p06-sql-integration/**`. If it fails for any other reason, fix the test setup before changing the workflow.

## Task 2: Extend only the existing artifact upload

**Files:**

- Modify: `.github/workflows/publish-alpha.yml`
- Test: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Add both independent real-profile roots**

Keep the existing publisher, token permissions, release gates, version, push loop, artifact name, `if: always()`, error policy, and retention unchanged. Extend only the upload path block:

```yaml
          path: |
            artifacts/release/**
            artifacts/verify/**
            artifacts/p05-integration/**
            artifacts/p06-sql-integration/**
```

- [ ] **Step 2: Run the focused test and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~P08_PublicationWorkflow_AlwaysPreservesCompleteEvidence"
```

Expected: PASS, one test.

- [ ] **Step 3: Run the complete architecture project**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release
```

Expected: all architecture tests pass; the count is the previous 9 plus the new publication guard.

- [ ] **Step 4: Inspect and commit only the implementation files**

```powershell
git diff --check
git diff -- .github/workflows/publish-alpha.yml tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git add -- .github/workflows/publish-alpha.yml tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git diff --cached --check
git commit -m "ci(platform): preserve complete P08 publication evidence"
```

Expected: the commit contains one test and two additional upload roots only.

## Task 3: Validate and review the planning/implementation branch

**Files:**

- Verify: all files changed from `origin/main`
- Verify: `.github/workflows/publish-alpha.yml`
- Verify: `eng/pack-release.ps1`
- Verify: `Directory.Build.props`

- [ ] **Step 1: Run every local non-container release gate**

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate E2E -Profile release
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
```

Expected: every command exits 0. Build has zero warnings and errors; test counts must not fall below the current baseline.

- [ ] **Step 2: Check whether local real-profile execution is available**

```powershell
docker info
```

If the Docker engine is healthy, run both exact gates:

```powershell
pwsh ./eng/verify.ps1 -Gate Integration -Profile p05-real
pwsh ./eng/verify.ps1 -Gate Integration -Profile p06-real
```

If Docker remains unavailable because of the known local Docker Desktop backend fault, record that fact and stop local container work. Do not reset Docker Desktop, delete images/volumes, or modify user Docker state. The two dedicated Ubuntu PR/main jobs remain mandatory and authoritative.

- [ ] **Step 3: Recheck version, publisher, and packer invariants**

```powershell
git grep -n -I "0.8.0-alpha.1" -- Directory.Build.props eng/pack-release.ps1 eng/verify.ps1 .github/workflows/publish-alpha.yml
git grep -n -I -E "skip-duplicate|nuget\.org|NUGET_AUTH_TOKEN" -- .github/workflows/publish-alpha.yml NuGet.config
```

Expected: all release authorities use `0.8.0-alpha.1`; there is no `--skip-duplicate`; the only publish destination is `nuget.pkg.github.com/GTX537`; the token exists only as the publication step environment value.

- [ ] **Step 4: Review the complete branch diff and history**

```powershell
git fetch origin
git status --short --branch
git log --oneline origin/main..HEAD
git diff --check origin/main...HEAD
git diff --stat origin/main...HEAD
git diff origin/main...HEAD
```

Expected: only the approved S02 design, plan, workflow correction, and architecture test are present. If `origin/main` moved, merge the fetched `origin/main` into this task branch, rerun all relevant gates, and do not rewrite shared history.

## Task 4: Land the planning/implementation PR and validate exact main

**Files:** none locally beyond the reviewed branch.

- [ ] **Step 1: Push the task branch and open one Platform PR**

```powershell
git push -u origin codex/p08-s02-publication-design
gh pr create --repo GTX537/CP6.Platform --base main --head codex/p08-s02-publication-design --title "ci(platform): prepare complete P08 immutable publication evidence" --body "Implements the approved P08-S02 design: locks complete release, verify, P05-real, and P06-real evidence in the existing exact-main publisher. No package publication occurs in this PR."
```

Record the PR number, URL, and full head SHA:

```powershell
$planningPr = gh pr view --repo GTX537/CP6.Platform codex/p08-s02-publication-design --json number,url,headRefOid | ConvertFrom-Json
$planningPr
```

- [ ] **Step 2: Wait for all required PR jobs**

```powershell
gh pr checks --repo GTX537/CP6.Platform $planningPr.number --watch
```

Required success jobs: Windows, Ubuntu, Ubuntu real Dapr/Kafka, and Ubuntu real SQL Server. A skipped, cancelled, stale, neutral, or red job is not success.

- [ ] **Step 3: Re-review the GitHub PR diff before merge**

```powershell
gh pr diff --repo GTX537/CP6.Platform $planningPr.number
gh pr view --repo GTX537/CP6.Platform $planningPr.number --json mergeStateStatus,reviewDecision,statusCheckRollup
```

Expected: no secrets, generated evidence, machine paths, unrelated edits, or scope drift.

- [ ] **Step 4: Merge without deleting or rewriting branches**

```powershell
gh pr merge --repo GTX537/CP6.Platform $planningPr.number --merge
git fetch origin
$publicationMainSha = (git rev-parse origin/main).Trim()
$publicationMainSha
```

Expected: `$publicationMainSha` is the PR merge commit and contains the three task commits. Do not force-push or delete the remote branch.

- [ ] **Step 5: Wait for the exact post-merge main run**

```powershell
$mainRuns = gh run list --repo GTX537/CP6.Platform --workflow platform-validation.yml --branch main --event push --limit 20 --json databaseId,url,headSha,status,conclusion | ConvertFrom-Json
$planningMainRun = $mainRuns | Where-Object { $_.headSha -eq $publicationMainSha } | Select-Object -First 1
$planningMainRun
gh run watch --repo GTX537/CP6.Platform $planningMainRun.databaseId --exit-status
gh run view --repo GTX537/CP6.Platform $planningMainRun.databaseId --json databaseId,url,headSha,status,conclusion,jobs
```

Expected: run head equals `$publicationMainSha`; all four required jobs conclude `success`. Do not dispatch publication if any identity or conclusion differs.

## Task 5: Authorize and dispatch the sole exact-main publisher

**Files:** read-only inspection of the merged `main` workflow and packer.

- [ ] **Step 1: Confirm exact remote authority immediately before dispatch**

```powershell
git fetch origin
$approvedMainSha = (git rev-parse origin/main).Trim()
if ($approvedMainSha -ne $publicationMainSha) { throw "Platform main moved before publication approval." }
if ($planningMainRun.headSha -ne $approvedMainSha -or $planningMainRun.conclusion -ne "success") { throw "The exact approved main run is not green." }
git show "$approvedMainSha`:.github/workflows/publish-alpha.yml"
git show "$approvedMainSha`:eng/pack-release.ps1"
```

Expected: the merged workflow has exactly four artifact roots, exact-main checks, all release gates, five ordinary pushes, no `--skip-duplicate`, and version `0.8.0-alpha.1`.

- [ ] **Step 2: Dispatch once from `main` with the full approved SHA**

```powershell
$dispatchStartedAt = [DateTimeOffset]::UtcNow
gh workflow run publish-alpha.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$approvedMainSha
```

Do not dispatch a second run while the first is queued or running.

- [ ] **Step 3: Resolve and bind the dispatched run**

```powershell
$publishRuns = gh run list --repo GTX537/CP6.Platform --workflow publish-alpha.yml --branch main --event workflow_dispatch --limit 20 --json databaseId,url,headSha,createdAt,status,conclusion | ConvertFrom-Json
$publishRun = $publishRuns |
    Where-Object { $_.headSha -eq $approvedMainSha -and [DateTimeOffset]$_.createdAt -ge $dispatchStartedAt.AddMinutes(-1) } |
    Sort-Object createdAt -Descending |
    Select-Object -First 1
$publishRun
```

Expected: exactly one new run matches the approved SHA and dispatch window. If selection is ambiguous, inspect the run list and bind manually before continuing; never guess a run ID.

- [ ] **Step 4: Watch the run and preserve failure semantics**

```powershell
gh run watch --repo GTX537/CP6.Platform $publishRun.databaseId --exit-status
```

If it fails, inspect the job and logs before any action:

```powershell
gh run view --repo GTX537/CP6.Platform $publishRun.databaseId --json databaseId,url,headSha,status,conclusion,jobs
gh run view --repo GTX537/CP6.Platform $publishRun.databaseId --log-failed
```

If any push may have started, stop S02 and preserve the run/artifact; do not retry or delete packages. If failure occurred before upload, fix forward on a new reviewed commit and repeat exact-main approval.

- [ ] **Step 5: Reject main drift at terminal observation**

```powershell
git fetch origin
$terminalMainSha = (git rev-parse origin/main).Trim()
if ($terminalMainSha -ne $approvedMainSha) { throw "Platform main drifted during publication; this run cannot be accepted." }
```

If upload may have occurred, treat drift as a burned-version incident and stop for a new version decision.

## Task 6: Verify run, artifact, package set, and hashes independently

**Files:** downloaded only under a new temporary directory; no repository files changed.

- [ ] **Step 1: Capture terminal run and job facts**

```powershell
$publishFacts = gh run view --repo GTX537/CP6.Platform $publishRun.databaseId --json databaseId,url,event,headBranch,headSha,status,conclusion,jobs | ConvertFrom-Json
$publishFacts
if ($publishFacts.event -ne "workflow_dispatch" -or $publishFacts.headBranch -ne "main" -or $publishFacts.headSha -ne $approvedMainSha -or $publishFacts.conclusion -ne "success") {
    throw "Publication run identity or conclusion is invalid."
}
```

Inspect the full log and confirm all five ordinary package pushes completed successfully:

```powershell
gh run view --repo GTX537/CP6.Platform $publishRun.databaseId --log | Select-String -Pattern "CP6.Platform.|push|published|success" -CaseSensitive:$false
```

- [ ] **Step 2: Capture artifact API identity and digest**

```powershell
$artifactResponse = gh api "repos/GTX537/CP6.Platform/actions/runs/$($publishRun.databaseId)/artifacts" | ConvertFrom-Json
$artifactName = "p08-alpha-$approvedMainSha"
$artifact = @($artifactResponse.artifacts | Where-Object { $_.name -eq $artifactName })
if ($artifact.Count -ne 1) { throw "Expected exactly one $artifactName artifact." }
if ($artifact[0].expired) { throw "Publication artifact is expired." }
if ($artifact[0].digest -notmatch "^(sha256:)?[0-9a-f]{64}$") { throw "Artifact digest is missing or malformed." }
$artifact[0] | Select-Object id,name,size_in_bytes,digest,created_at,expires_at,expired
```

Record the exact artifact ID, API digest, size, creation time, and expiry boundary. The API digest is evidence metadata; do not claim that an extracted directory reproduces the server-side archive digest.

- [ ] **Step 3: Download into a fresh temporary evidence directory**

```powershell
$evidenceDirectory = [IO.Path]::Combine([IO.Path]::GetTempPath(), "cp6-p08-s02-$($publishRun.databaseId)")
if (Test-Path -LiteralPath $evidenceDirectory) { throw "Evidence directory already exists: $evidenceDirectory" }
New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
gh run download --repo GTX537/CP6.Platform $publishRun.databaseId --name $artifactName --dir $evidenceDirectory
Get-ChildItem -LiteralPath $evidenceDirectory -Recurse | Select-Object FullName,Length
```

Expected extraction roots are `release`, `verify`, `p05-integration`, and `p06-sql-integration`. If GitHub returns a different layout, inspect and document it; do not relax file-identity checks.

- [ ] **Step 4: Verify the exact ten packages and manifest**

```powershell
$releaseDirectory = Join-Path $evidenceDirectory "release"
$expectedPackageIds = @(
    "CP6.Platform.Contracts",
    "CP6.Platform.Abstractions",
    "CP6.Platform.AspNetCore",
    "CP6.Platform.Messaging",
    "CP6.Platform.EntityFramework"
)
$expectedPackageNames = @($expectedPackageIds | ForEach-Object {
    "$_.0.8.0-alpha.1.nupkg"
    "$_.0.8.0-alpha.1.snupkg"
} | Sort-Object)
$actualPackages = @(Get-ChildItem -LiteralPath $releaseDirectory -File | Where-Object { $_.Name -like "*.nupkg" -or $_.Name -like "*.snupkg" } | Sort-Object Name)
if (($actualPackages.Name | ConvertTo-Json -Compress) -ne ($expectedPackageNames | ConvertTo-Json -Compress)) {
    throw "Downloaded package set is not the exact approved ten-file set."
}
if ($actualPackages | Where-Object Length -LE 0) { throw "A package file is empty." }

$manifestPath = Join-Path $releaseDirectory "sha256.json"
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if ($manifest.Count -ne 10) { throw "sha256.json must contain exactly ten entries." }
foreach ($package in $actualPackages) {
    $entry = @($manifest | Where-Object { $_.file -eq $package.Name })
    if ($entry.Count -ne 1) { throw "Manifest entry mismatch for $($package.Name)." }
    $actualHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
    if ($actualHash -ne $entry[0].sha256) { throw "SHA-256 mismatch for $($package.Name)." }
}
$manifest | Sort-Object file | Format-Table file,sha256
```

Expected: exact five ordinary packages, five symbols, one manifest, and ten independently matching hashes. Copy the five ordinary hashes exactly into the evidence ledger later.

- [ ] **Step 5: Verify complete real-profile and release evidence**

```powershell
$requiredEvidence = @(
    (Join-Path $evidenceDirectory "p05-integration/result.json"),
    (Join-Path $evidenceDirectory "p05-integration/docker-compose.log"),
    (Join-Path $evidenceDirectory "p06-sql-integration/result.json"),
    (Join-Path $evidenceDirectory "p06-sql-integration/sql-server.log")
)
foreach ($path in $requiredEvidence) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing evidence: $path" }
    if ((Get-Item -LiteralPath $path).Length -le 0) { throw "Empty evidence: $path" }
}
Get-Content -LiteralPath (Join-Path $evidenceDirectory "p05-integration/result.json") -Raw | ConvertFrom-Json
Get-Content -LiteralPath (Join-Path $evidenceDirectory "p06-sql-integration/result.json") -Raw | ConvertFrom-Json
Get-ChildItem -LiteralPath (Join-Path $evidenceDirectory "verify") -Recurse -File | Select-Object FullName,Length
```

Expected: both result documents report successful real runs, their logs are non-empty, and release gate summaries/logs are present. A skipped or synthesized result is invalid.

- [ ] **Step 6: Re-run package content verification against the downloaded files**

Inspect the downloaded archives without executing package content:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ordinaryPackages = @($actualPackages | Where-Object { $_.Name -notlike "*.snupkg" })
foreach ($packageId in $expectedPackageIds) {
    $package = @($ordinaryPackages | Where-Object { $_.Name -eq "$packageId.0.8.0-alpha.1.nupkg" })
    if ($package.Count -ne 1) { throw "Ordinary package mismatch for $packageId." }

    $archive = [IO.Compression.ZipFile]::OpenRead($package[0].FullName)
    try {
        $entries = @($archive.Entries)
        $assembly = @($entries | Where-Object { $_.FullName -eq "lib/net8.0/$packageId.dll" -and $_.Length -gt 0 })
        if ($assembly.Count -ne 1) { throw "$packageId has no non-empty runtime assembly." }
        if ($entries.FullName | Where-Object { $_ -match "(^|/)(tests?|CP6\.Platform\.Testing)(/|$)" -or $_ -match "\.Tests(?:\.|/)" }) {
            throw "$packageId contains a testing entry."
        }
        if ($entries.FullName | Where-Object { $_ -match "^[A-Za-z]:[\\/]" -or $_ -match "^/(home|Users)/" }) {
            throw "$packageId contains a machine-specific entry path."
        }
        if ($packageId -ne "CP6.Platform.Contracts" -and
            ($entries.FullName | Where-Object { $_.StartsWith("contracts/observability/", [StringComparison]::Ordinal) })) {
            throw "$packageId illegally owns P08 observability assets."
        }
        if ($packageId -ne "CP6.Platform.Messaging" -and
            ($entries.FullName | Where-Object { $_ -eq "contracts/contract-bundle.v1.json" -or $_.StartsWith("contracts/events/", [StringComparison]::Ordinal) })) {
            throw "$packageId illegally owns P04 event assets."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$contractsArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseDirectory "CP6.Platform.Contracts.0.8.0-alpha.1.nupkg"))
try {
    $contractEntries = @($contractsArchive.Entries.FullName)
    foreach ($required in @(
        "contracts/observability/slo-evidence/v1/assets.v1.json",
        "contracts/observability/slo-evidence/v1/schema.json",
        "contracts/observability/slo-evidence/v1/examples/non-candidate-indeterminate.json",
        "contracts/observability/slo-evidence/v1/examples/partial-indeterminate.json",
        "contracts/observability/slo-evidence/v1/examples/pii-negative.json",
        "contracts/observability/slo-evidence/v1/examples/valid-pass.json"
    )) {
        if ($required -notin $contractEntries) { throw "Contracts is missing $required." }
    }
}
finally {
    $contractsArchive.Dispose()
}

$messagingArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseDirectory "CP6.Platform.Messaging.0.8.0-alpha.1.nupkg"))
try {
    $messagingEntries = @($messagingArchive.Entries.FullName)
    foreach ($required in @(
        "contracts/contract-bundle.v1.json",
        "contracts/events/platform/contract-example-changed/v1/schema.json",
        "contracts/events/platform/contract-example-changed/v1/examples/valid.json",
        "contracts/events/platform/contract-example-changed/v1/examples/missing-required.json",
        "contracts/events/platform/contract-example-changed/v1/examples/unknown-optional.json",
        "contracts/events/platform/contract-example-changed/v1/examples/wrong-type.json",
        "contracts/events/platform/contract-example-changed/v1/examples/pii-negative.json"
    )) {
        if ($required -notin $messagingEntries) { throw "Messaging is missing $required." }
    }
}
finally {
    $messagingArchive.Dispose()
}
```

Expected: all structural, runtime assembly, asset ownership, test-asset, and machine-entry checks pass. The successful workflow pack step supplies the matching deep text-content safety check; confirm that step succeeded in the bound run logs. Record the verified exact filenames and hashes.

## Task 7: Record S02 evidence on a separate branch and PR

**Files:**

- Modify: `docs/P08-PUBLICATION.md`
- Modify: `docs/P08-OBSERVABILITY-RESILIENCE.md`
- Modify: `docs/runbooks/P08-TRACE-EXPORTER.md`
- Modify: `docs/runbooks/P08-HEALTH-READINESS.md`
- Modify: `docs/runbooks/P08-HTTP-RESILIENCE.md`
- Modify: `docs/runbooks/P08-MESSAGING-BACKLOG.md`
- Modify: `docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md`
- Modify: `CHANGELOG.md`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Create a clean evidence worktree from the exact published main**

```powershell
git -C D:\CP6\CP6.Platform fetch origin
if ((git -C D:\CP6\CP6.Platform rev-parse origin/main).Trim() -ne $approvedMainSha) { throw "Platform main changed before evidence branch creation." }
git -C D:\CP6\CP6.Platform worktree add -b codex/p08-s02-publication-evidence D:\CP6-worktrees\p08-s02-publication-evidence origin/main
git -C D:\CP6-worktrees\p08-s02-publication-evidence status --short --branch
```

Do not reuse the publication-planning branch and do not delete either worktree.

- [ ] **Step 2: Change the documentation contract first and confirm RED**

In `RepositoryArchitectureTests.P08_Documentation_IsCompleteAndSafe`, change the required stage line to:

```csharp
"P08 status: S00-S02 complete; S03-S06 pending."
```

Add the exact planning-main and publication run URLs to the explicit safe URL removal list so the safety guard remains closed to every other URL. Then run:

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~P08_Documentation_IsCompleteAndSafe"
```

Expected: FAIL because the seven P08 documents still state `S00-S01 complete; S02-S06 pending`.

- [ ] **Step 3: Update all seven status lines and the durable publication ledger**

Change every required P08 document to exactly:

```text
P08 status: S00-S02 complete; S03-S06 pending.
```

Expand `docs/P08-PUBLICATION.md` with exact, non-placeholder evidence:

- planning PR number/URL, head SHA, merge SHA, and all four PR jobs;
- exact planning post-merge main run ID/URL and four successful jobs;
- approved/full source main SHA;
- publication run ID/URL, event/ref/head, terminal success, and five successful pushes;
- artifact ID, exact name, API digest, size, creation/expiry values, and 30-day policy;
- exact ten filenames and five ordinary package SHA-256 values;
- confirmation that manifest hashes, runtime assemblies, asset ownership, content safety, P05 evidence, and P06 evidence passed;
- evidence limitations: API digest is recorded as GitHub metadata, package hashes were independently recomputed, and S03-S06 are still pending;
- stage ledger with S00-S02 complete and S03-S06 pending; never write `Frozen / Consumable`.

Add a P08 changelog bullet stating that the five immutable packages were published from the exact main SHA with complete P05/P06 and digest evidence, while CRM consumption and freeze remain S03-S06.

No literal placeholders such as `<run-id>`, `TODO`, `TBD`, or synthetic hashes may remain.

- [ ] **Step 4: Run the focused documentation test and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~P08_Documentation_IsCompleteAndSafe"
```

Expected: PASS.

- [ ] **Step 5: Run all evidence-branch gates**

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate E2E -Profile release
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
git grep -n -I -E "TODO|TBD|FIXME|<run-id>|<sha>|<hash>" -- docs/P08-PUBLICATION.md docs/P08-OBSERVABILITY-RESILIENCE.md docs/runbooks CHANGELOG.md
git diff --check
```

Expected: all non-container gates pass and the placeholder scan is empty. Dedicated PR/main jobs will rerun both real profiles.

- [ ] **Step 6: Review and commit only the evidence files**

```powershell
git status --short
git diff -- docs/P08-PUBLICATION.md docs/P08-OBSERVABILITY-RESILIENCE.md docs/runbooks CHANGELOG.md tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git add -- docs/P08-PUBLICATION.md docs/P08-OBSERVABILITY-RESILIENCE.md docs/runbooks/P08-TRACE-EXPORTER.md docs/runbooks/P08-HEALTH-READINESS.md docs/runbooks/P08-HTTP-RESILIENCE.md docs/runbooks/P08-MESSAGING-BACKLOG.md docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md CHANGELOG.md tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git diff --cached --check
git commit -m "docs(platform): record P08 immutable publication evidence"
```

Expected: only S02 evidence/status/test changes are staged and committed.

## Task 8: Land the evidence PR and close only S02

**Files:** none locally beyond the reviewed evidence branch.

- [ ] **Step 1: Push and open the evidence PR**

```powershell
git push -u origin codex/p08-s02-publication-evidence
gh pr create --repo GTX537/CP6.Platform --base main --head codex/p08-s02-publication-evidence --title "docs(platform): record P08 immutable publication evidence" --body "Records the exact P08-S02 main, workflow, artifact, package, SHA-256, P05-real, and P06-real evidence. Closes S02 only; S03-S06 remain pending and P08 is not Frozen / Consumable."
$evidencePr = gh pr view --repo GTX537/CP6.Platform codex/p08-s02-publication-evidence --json number,url,headRefOid | ConvertFrom-Json
$evidencePr
```

- [ ] **Step 2: Wait for all four PR jobs and review the complete diff**

```powershell
gh pr checks --repo GTX537/CP6.Platform $evidencePr.number --watch
gh pr diff --repo GTX537/CP6.Platform $evidencePr.number
gh pr view --repo GTX537/CP6.Platform $evidencePr.number --json mergeStateStatus,reviewDecision,statusCheckRollup
```

Expected: Windows, Ubuntu, real Dapr/Kafka, and real SQL Server jobs all succeed; evidence exactly matches captured APIs/artifact files and contains no secret or scope drift.

- [ ] **Step 3: Merge and validate the final post-evidence main commit**

```powershell
gh pr merge --repo GTX537/CP6.Platform $evidencePr.number --merge
git fetch origin
$s02MainSha = (git rev-parse origin/main).Trim()
$finalRuns = gh run list --repo GTX537/CP6.Platform --workflow platform-validation.yml --branch main --event push --limit 20 --json databaseId,url,headSha,status,conclusion | ConvertFrom-Json
$s02MainRun = $finalRuns | Where-Object { $_.headSha -eq $s02MainSha } | Select-Object -First 1
$s02MainRun
gh run watch --repo GTX537/CP6.Platform $s02MainRun.databaseId --exit-status
gh run view --repo GTX537/CP6.Platform $s02MainRun.databaseId --json databaseId,url,headSha,status,conclusion,jobs
```

Expected: exact post-evidence main head and all four jobs succeed. Do not delete remote branches.

- [ ] **Step 4: Perform the terminal S02 audit**

Verify all of the following together:

```powershell
git fetch origin
git show "$s02MainSha`:docs/P08-PUBLICATION.md"
gh pr view --repo GTX537/CP6.Platform $planningPr.number --json state,mergedAt,mergeCommit,statusCheckRollup
gh run view --repo GTX537/CP6.Platform $planningMainRun.databaseId --json headSha,conclusion,jobs
gh run view --repo GTX537/CP6.Platform $publishRun.databaseId --json event,headBranch,headSha,conclusion,jobs
gh pr view --repo GTX537/CP6.Platform $evidencePr.number --json state,mergedAt,mergeCommit,statusCheckRollup
gh run view --repo GTX537/CP6.Platform $s02MainRun.databaseId --json headSha,conclusion,jobs
```

Completion criteria:

1. planning/implementation PR merged and its exact main run has four successful jobs;
2. one exact-main publication run succeeded while main remained unchanged;
3. five ordinary `0.8.0-alpha.1` packages were pushed without duplicate skipping;
4. exact ten packages, manifest, independent hashes, artifact identity/digest, P05 evidence, and P06 evidence were verified;
5. evidence PR merged and its exact post-merge main run has four successful jobs;
6. Platform main states only `S00-S02 complete; S03-S06 pending`.

After these checks, report S02 complete with exact PR/run/commit/artifact/hash evidence and proceed to a separately reviewed P08-S03 CRM fixed-version consumption plan. Do not claim P08 is `Frozen / Consumable` and do not perform S03 changes under either S02 branch.
