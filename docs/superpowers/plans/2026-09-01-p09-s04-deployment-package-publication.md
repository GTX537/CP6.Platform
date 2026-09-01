# P09-S04 Deployment Package Publication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish exactly `CP6.Platform.Deployment 0.9.0-alpha.1` once from an exact, fully validated Platform `main` commit to the authoritative GitHub Packages feed, independently verify the accepted bytes, and record durable P09-S04 evidence without advancing CRM consumption or deployment scope.

**Architecture:** Add a P09-only manually dispatched GitHub Actions workflow while preserving the historical P08 publisher unchanged. Put deterministic manifest construction and Registry availability/verification behavior in small PowerShell entry points under `eng/p09`, contract-test the workflow and scripts from `CP6.Platform.DeploymentTests`, merge the implementation first, require the exact post-merge five-job validation run, then dispatch once. The publication job builds and validates one package, rejects version collisions before mutation, pushes the already-validated ordinary package exactly once, downloads that same fixed Registry version, and proves byte equality. A separate evidence PR records immutable run, artifact, package, Registry, and hash identities.

**Tech Stack:** GitHub Actions, PowerShell 7, .NET 8, NuGet v3/GitHub Packages, xUnit, GitHub CLI, Docker-backed P05/P06/P09 gates.

---

## Execution boundary

All implementation work is performed directly in:

```text
D:\CP6.Platform-worktrees\p09-package-publication
codex/p09-package-publication
```

The branch began clean at exact Platform `origin/main` commit `1d1b9b55bd81d285f48ca3fe3ad5b5aadaff51c0`, whose five-job exact-main validation run `33474254564` succeeded. GitHub's package API returned `404 Package not found` for `CP6.Platform.Deployment`, so the package and requested version were absent during initial read-only planning. Availability must be checked again inside the publication transaction; this planning observation is not publication authority.

This task may mutate only the GitHub Packages entry for `CP6.Platform.Deployment 0.9.0-alpha.1`, and only after the implementation PR and exact-main validation succeed. It may not modify or republish the five P08 `0.8.0-alpha.2` packages, use another Registry, deploy an environment, add a CRM runtime registration, business topic, route, or worker, or claim P09 `Frozen / Consumable`.

If an upload result is uncertain, query the Registry before taking another action. Never use duplicate skipping, overwrite, delete, unlist, force-push, or remote branch deletion. If any bytes were accepted but cannot be verified, preserve the incident and stop for a forward-version decision.

## Task 1: Lock the P09 publication transaction with failing contract tests

**Files:**

- Create: `tests/CP6.Platform.DeploymentTests/P09PublicationWorkflowTests.cs`
- Inspect: `.github/workflows/publish-alpha.yml`
- Inspect: `eng/pack-p09.ps1`

- [ ] **Step 1: Add a failing workflow contract test**

Create `P09PublicationWorkflowTests` with helpers that load repository files and split the publication workflow into named step regions. Add `Workflow_FreezesExactMainSinglePackageTransaction` and require all of the following literals and ordering:

```text
name: publish-p09-deployment
workflow_dispatch
expected_commit
permissions:
contents: read
packages: write
refs/heads/main
git rev-parse origin/main
./eng/verify.ps1 -Gate Format -Profile ci
./eng/verify.ps1 -Gate Build -Profile ci
./eng/verify.ps1 -Gate Unit -Profile ci
./eng/verify.ps1 -Gate Integration -Profile p05-real
./eng/verify.ps1 -Gate Integration -Profile p06-real
./eng/verify.ps1 -Gate E2E -Profile ci
./eng/verify.ps1 -Gate Contract -Profile ci
./eng/verify.ps1 -Gate Security -Profile ci
./eng/verify.ps1 -P09Real -Profile ci -ExpectedGitSha
./eng/pack-p09.ps1 -VerifyReproducible
CP6.Platform.Deployment.0.9.0-alpha.1.nupkg
https://nuget.pkg.github.com/GTX537/index.json
```

Assert the version-availability step occurs before `dotnet nuget push`; candidate manifest creation occurs before the push; Registry verification occurs after the push; the workflow contains exactly one `dotnet nuget push`, does not contain `--skip-duplicate`, does not glob all ordinary packages, and does not mention P08 package IDs in its push step.

- [ ] **Step 2: Add failing evidence-retention and credential-boundary tests**

Require one `if: always()` artifact upload named `p09-publication-${{ inputs.expected_commit }}` with these exact roots:

```text
artifacts/p09-package/**
artifacts/p09-publication/**
artifacts/verify/**
artifacts/p05-integration/**
artifacts/p06-sql-integration/**
artifacts/p09-rehearsal/**
artifacts/p09-kubernetes/**
```

Require the package token to appear only in the Registry preflight, push, and Registry verification step environments, never in the candidate manifest command, artifact path, or workflow output. Require `persist-credentials: false`, pinned actions, `if-no-files-found: error`, and 30-day retention.

- [ ] **Step 3: Add failing script contract tests**

Add `ManifestBuilder_RequiresExactCandidateAndPassedEvidence` and `RegistryVerifier_SeparatesAvailabilityAndPublishedVerification`. Require the future scripts to freeze:

```text
CP6.Platform.Deployment
0.9.0-alpha.1
https://nuget.pkg.github.com/GTX537/index.json
https://api.github.com/users/GTX537/packages/nuget/CP6.Platform.Deployment/versions
available
published
packageSha256
registryVersionId
```

Also reject `--skip-duplicate`, deletion verbs, alternate feeds, credential serialization, and any rebuild/pack command in the Registry verifier.

- [ ] **Step 4: Run the focused tests and confirm RED**

```powershell
$env:DOTNET_HOST_PATH = 'C:\Users\tt\.dotnet\dotnet.exe'
& $env:DOTNET_HOST_PATH test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --configuration Release --filter 'FullyQualifiedName~P09PublicationWorkflowTests'
```

Expected: FAIL because `.github/workflows/publish-p09.yml`, `eng/p09/New-P09PublicationManifest.ps1`, and `eng/p09/Test-P09RegistryPackage.ps1` do not exist.

## Task 2: Build and validate the candidate manifest

**Files:**

- Create: `eng/p09/New-P09PublicationManifest.ps1`
- Modify: `tests/CP6.Platform.DeploymentTests/P09PublicationWorkflowTests.cs`

- [ ] **Step 1: Add test fixtures and an executable manifest test**

Create a temporary fixture containing one ordinary package, optional symbol package, passed gate summaries, passed P05/P06 result documents, one exact P09 rehearsal Evidence document, and one matching Kubernetes result. Invoke the script as a child PowerShell process and assert its output JSON has this fixed shape:

```json
{
  "schemaVersion": 1,
  "status": "Candidate",
  "source": { "gitSha": "<40 lowercase hex>", "workflowRunId": "<digits>", "workflowRunAttempt": 1, "workflowJob": "publish" },
  "package": { "id": "CP6.Platform.Deployment", "version": "0.9.0-alpha.1", "file": "CP6.Platform.Deployment.0.9.0-alpha.1.nupkg", "sha256": "<64 lowercase hex>" },
  "symbols": { "file": "CP6.Platform.Deployment.0.9.0-alpha.1.snupkg", "sha256": "<64 lowercase hex>" },
  "runtime": { "profileId": "cp6-platform-p09-ci-v1", "profileSha256": "<64 lowercase hex>", "composeManifestSha256": "<64 lowercase hex>", "kubernetesManifestSha256": "<64 lowercase hex>", "evidenceSha256": "<64 lowercase hex>", "daprImageDigest": "sha256:<64 hex>", "kafkaImageDigest": "sha256:<64 hex>", "kubectlImageDigest": "sha256:<64 hex>" },
  "gates": [{ "name": "...", "path": "...", "status": "Passed" }],
  "registry": { "authority": "GitHub Packages", "source": "https://nuget.pkg.github.com/GTX537/index.json", "packageUrl": "https://github.com/users/GTX537/packages/nuget/CP6.Platform.Deployment" }
}
```

Add negative fixtures for wrong SHA, wrong package version/name/count, failed or mismatched Evidence, mismatched Kubernetes hash, missing P05/P06 results, non-passed gate summary, and secret-bearing Registry values.

- [ ] **Step 2: Implement strict manifest construction**

`New-P09PublicationManifest.ps1` accepts exact source/run/job values plus package, verification, P05, P06, rehearsal, and Kubernetes roots. It must:

1. validate exact lowercase Git SHA and numeric run identity;
2. find exactly one approved ordinary package and at most one exact symbol package;
3. recompute hashes without trusting pack output;
4. require one `Passed` rehearsal bound to the same SHA/version, zero residue, and one matching passed Kubernetes result;
5. require every declared gate/result document to be `Passed`;
6. use repository-relative forward-slash paths only;
7. serialize no token, credential, machine path, trace token, or secret-like value;
8. write canonical depth-sufficient JSON to `artifacts/p09-publication/candidate-manifest.v1.json`.

- [ ] **Step 3: Run focused tests until GREEN**

```powershell
& $env:DOTNET_HOST_PATH test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --configuration Release --filter 'FullyQualifiedName~P09PublicationWorkflowTests&Name~Manifest'
```

Expected: all manifest positive and negative cases pass.

## Task 3: Implement collision rejection and independent Registry verification

**Files:**

- Create: `eng/p09/Test-P09RegistryPackage.ps1`
- Modify: `tests/CP6.Platform.DeploymentTests/P09PublicationWorkflowTests.cs`

- [ ] **Step 1: Add executable HTTP-contract tests**

Use an in-process loopback HTTP fixture or a deterministic injected endpoint/base-address fixture. Cover:

- `-Mode Available` accepts a package-level 404;
- `-Mode Available` accepts a package whose paginated versions exclude `0.9.0-alpha.1`;
- `-Mode Available` rejects exactly one or multiple matching versions;
- `-Mode Published` requires exactly one matching Registry version;
- `-Mode Published` downloads from the NuGet v3 `PackageBaseAddress/3.0.0` resource and matches candidate SHA/content;
- `-Mode Published` rejects a changed hash, wrong package contents, wrong ID/version, missing runtime DLL/assets, duplicate Registry versions, and authentication/API failures;
- token text never appears in stdout, stderr, output JSON, or exception messages.

- [ ] **Step 2: Implement the Registry verifier**

The script reads the token only from `GITHUB_TOKEN`, sends it in headers only, and supports exactly:

```powershell
./eng/p09/Test-P09RegistryPackage.ps1 -Mode Available
./eng/p09/Test-P09RegistryPackage.ps1 -Mode Published -CandidateManifestPath artifacts/p09-publication/candidate-manifest.v1.json -OutputPath artifacts/p09-publication/registry-verification.v1.json -DownloadDirectory artifacts/p09-publication/download
```

For `Available`, page the GitHub package-version API and fail closed on any exact version collision. For `Published`, independently query exactly one version, resolve the authenticated NuGet v3 package base address, download the fixed lowercase ID/version `.nupkg`, recompute SHA-256, and rerun the same package-shape safety checks without executing package contents. Write only non-secret facts: version ID/name, creation/update timestamps, download SHA, byte length, package-content result, and `Verified` status.

- [ ] **Step 3: Run focused tests until GREEN**

```powershell
& $env:DOTNET_HOST_PATH test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --configuration Release --filter 'FullyQualifiedName~P09PublicationWorkflowTests&Name~Registry'
```

Expected: all Registry state-machine, byte-identity, package-shape, and secret-redaction tests pass.

## Task 4: Add the P09-only exact-main publisher

**Files:**

- Create: `.github/workflows/publish-p09.yml`
- Modify: `tests/CP6.Platform.DeploymentTests/P09PublicationWorkflowTests.cs`
- Modify: `docs/P09-PUBLICATION.md`
- Modify if required by local gate reproduction: `eng/test-p09-kubernetes.ps1`
- Modify if required by local gate reproduction: `eng/run-p06-sql-integration.ps1`

- [ ] **Step 1: Implement the non-mutating and build-once phases**

Create one `publish` job on `ubuntu-latest`, manual dispatch only, `contents: read`, `packages: write`, 45-minute timeout, exact expected-commit checkout, pinned checkout/setup-dotnet/setup-docker/upload-artifact actions, and exact-main/ref assertions.

Run the eight established gates, P09 real rehearsal, and reproducible package generation in the exact runbook order. The package step must create only `artifacts/p09-package/CP6.Platform.Deployment.0.9.0-alpha.1.nupkg` plus its optional symbol evidence. Call `Test-P09RegistryPackage.ps1 -Mode Available` before any push and call `New-P09PublicationManifest.ps1` before mutation.

- [ ] **Step 2: Implement the one-way mutation and postcondition**

Push the one explicit ordinary package path exactly once:

```powershell
dotnet nuget push artifacts/p09-package/CP6.Platform.Deployment.0.9.0-alpha.1.nupkg --source 'https://nuget.pkg.github.com/GTX537/index.json' --api-key $env:NUGET_AUTH_TOKEN
```

Do not publish symbols, glob packages, rebuild, or use duplicate skipping. Immediately run `Test-P09RegistryPackage.ps1 -Mode Published` against the candidate manifest and preserve its independently downloaded package and result JSON.

- [ ] **Step 3: Preserve complete evidence on every outcome**

Upload the seven exact roots from Task 1 as one `p09-publication-${{ inputs.expected_commit }}` artifact with `if: always()`, error on no files, and 30-day retention. The pre-upload candidate manifest must remain available even if mutation or post-verification fails.

- [ ] **Step 4: Update the runbook to mark implementation ready, not published**

Change only the operational readiness text:

```text
Publication status: Ready for exact-main publication; no package has been uploaded by this implementation PR.
Stage: P09-S04 implementation ready; publication evidence pending.
```

Document the workflow filename, the two scripts, the no-retry uncertainty rule, and the evidence artifact shape. Do not write any run/package identity yet.

- [ ] **Step 5: Run all focused tests and review static safety**

```powershell
& $env:DOTNET_HOST_PATH test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --configuration Release --filter 'FullyQualifiedName~P09PublicationWorkflowTests'
rg -n -i 'skip-duplicate|nuget.org|delete|unlist|remove.*package|token.*manifest' .github/workflows/publish-p09.yml eng/p09/New-P09PublicationManifest.ps1 eng/p09/Test-P09RegistryPackage.ps1
git diff --check
```

Expected: focused tests pass; no forbidden mutation/alternate-feed pattern is present beyond explicit rejection assertions or explanatory comments.

If a required cross-platform gate fails before publication, add a focused regression test before making the smallest entry-point correction. In particular, Linux container shell text must use LF even from Windows worktrees, and nested .NET runners must honor `DOTNET_HOST_PATH` rather than selecting an unrelated system SDK.

## Task 5: Validate, review, commit, and land the implementation

**Files:** all Task 1-4 implementation and plan files only.

- [ ] **Step 1: Run proportional local gates**

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile ci
pwsh ./eng/verify.ps1 -Gate Build -Profile ci
pwsh ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh ./eng/verify.ps1 -Gate E2E -Profile ci
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
pwsh ./eng/verify.ps1 -Gate Security -Profile ci
pwsh ./eng/verify.ps1 -P09Contract -Profile ci
pwsh ./eng/pack-p09.ps1 -VerifyReproducible
```

Run local Docker P05/P06/P09 real gates if the engine is available and record any environment limitation honestly; the PR and exact-main hosted workflows remain mandatory regardless.

- [ ] **Step 2: Review and commit only S04 implementation**

```powershell
git status --short
git diff -- .github/workflows/publish-p09.yml eng/p09/New-P09PublicationManifest.ps1 eng/p09/Test-P09RegistryPackage.ps1 tests/CP6.Platform.DeploymentTests/P09PublicationWorkflowTests.cs docs/P09-PUBLICATION.md docs/superpowers/plans/2026-09-01-p09-s04-deployment-package-publication.md
git diff --check
git add -- .github/workflows/publish-p09.yml eng/p09/New-P09PublicationManifest.ps1 eng/p09/Test-P09RegistryPackage.ps1 tests/CP6.Platform.DeploymentTests/P09PublicationWorkflowTests.cs docs/P09-PUBLICATION.md docs/superpowers/plans/2026-09-01-p09-s04-deployment-package-publication.md
git diff --cached --check
git commit -m "ci(platform): prepare P09 immutable package publication"
```

- [ ] **Step 3: Push, open the PR, and require five green jobs**

```powershell
git push -u origin codex/p09-package-publication
gh pr create --repo GTX537/CP6.Platform --base main --head codex/p09-package-publication --title "ci(platform): prepare P09 immutable package publication" --body "Adds a P09-only exact-main, collision-rejecting, build-once publication transaction with independent Registry byte verification. This PR itself publishes no package and performs no deployment."
gh pr checks --repo GTX537/CP6.Platform <pr-number> --watch
```

Require `ubuntu-latest`, `windows-latest`, `ubuntu-dapr-kafka`, `ubuntu-sql-server`, and `ubuntu-p09-non-production-runtime` to succeed. Review the complete PR diff and exact head SHA before merge.

- [ ] **Step 4: Merge normally and validate exact main**

```powershell
gh pr merge --repo GTX537/CP6.Platform <pr-number> --merge
git fetch origin
$publicationMainSha = (git rev-parse origin/main).Trim()
gh run list --repo GTX537/CP6.Platform --workflow platform-validation.yml --branch main --event push --limit 20 --json databaseId,url,headSha,status,conclusion
gh run watch --repo GTX537/CP6.Platform <exact-main-run-id> --exit-status
```

Do not dispatch publication unless remote main is the implementation merge commit and all five exact-main jobs succeed.

## Task 6: Dispatch once and independently accept or reject the publication

**Files:** no repository changes; downloaded evidence uses a new temporary directory.

- [ ] **Step 1: Reconfirm authority and collision immediately before dispatch**

Require clean exact `origin/main`, the bound five-job successful main run, and another authenticated read-only package API query showing no exact `0.9.0-alpha.1` version. If the package/version exists, stop and inspect; never dispatch with a collision.

- [ ] **Step 2: Dispatch one workflow run**

```powershell
gh workflow run publish-p09.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$publicationMainSha
```

Bind exactly one new workflow-dispatch run by SHA and dispatch window, then watch it once to terminal state. If failure occurs after the push step begins, query the Registry and preserve evidence before deciding status; do not retry automatically.

- [ ] **Step 3: Reject main drift and capture terminal facts**

Require terminal remote main still equals the approved SHA. Capture workflow run ID/URL/event/ref/head/status/conclusion and the `publish` job ID/timestamps/conclusion. Require every pre-publication gate, availability check, pack, manifest, push, independent Registry verification, and artifact step to succeed.

- [ ] **Step 4: Download and independently audit the workflow artifact**

Capture the artifact API ID/name/digest/size/created/expiry facts, download into a fresh temp directory, and require exact roots. Recompute SHA-256 for candidate ordinary/symbol packages and independently downloaded Registry package; require all ordinary hashes equal the candidate manifest and Registry result. Validate candidate manifest source/run/job, P05/P06 conclusions, exact passed P09 Evidence, Profile/Compose/Kubernetes/Evidence hashes, three image digests, zero residue, Registry version ID, and package content shape.

- [ ] **Step 5: Query Registry independently after workflow success**

Use `gh api` outside the workflow to require exactly one `0.9.0-alpha.1` version for `CP6.Platform.Deployment`, record its immutable version ID and timestamps, and compare them with `registry-verification.v1.json`. No other P09 package and no P08 version may have changed.

## Task 7: Record P09-S04 evidence on a separate branch

**Files:**

- Modify: `docs/P09-PUBLICATION.md`
- Modify: `docs/P09-NON-PRODUCTION-RUNTIME.md`
- Modify: `CHANGELOG.md`
- Modify: `tests/CP6.Platform.DeploymentTests/P09DocumentationContractTests.cs`

- [ ] **Step 1: Create a new clean evidence worktree from exact published main**

Use branch `codex/p09-publication-evidence` in a new worktree. Do not reuse or delete implementation worktrees/branches.

- [ ] **Step 2: Update the documentation contract first and confirm RED**

Change the required P09 stage/status lines in `P09DocumentationContractTests` to `S01-S04 complete; S05-S06 pending` and `Published / Consumer Candidate`, plus non-placeholder publication identity requirements. Run the focused test and require failure before document edits.

- [ ] **Step 3: Record only observed immutable facts**

Write the implementation PR/head/merge identities and five PR/main jobs; publication run/job identities; artifact identity/digest; exact source SHA; candidate and downloaded package filenames/byte lengths/SHA-256; symbol identity if present; Profile/Compose/Kubernetes/Evidence hashes; image digests; gate summary conclusions; Registry package/version IDs and timestamps; and the independent post-download verification result. Explicitly state S05 CRM fixed-version consumption and S06 public reconciliation remain pending, and no deployment occurred.

- [ ] **Step 4: Validate, commit, PR, merge, and exact-main verify the evidence**

Run the focused documentation test, all non-container gates, P09 contract, placeholder/secret scan, and diff review. Commit only the four evidence files, push/open an evidence PR, require all five PR jobs, merge normally, and require all five exact post-evidence-main jobs. Retain remote branches.

## Completion criteria

P09-S04 is complete only when all conditions hold together:

1. the implementation PR and exact post-merge main five-job run succeeded;
2. one and only one exact-main P09 publication run completed successfully while main remained unchanged;
3. only `CP6.Platform.Deployment 0.9.0-alpha.1` ordinary bytes were pushed, with no duplicate skipping or rebuild;
4. Registry download SHA/content exactly matched the pre-upload candidate manifest;
5. candidate, gate, real-profile, P09 Evidence, image-digest, artifact, Registry version, and independent external query facts agree;
6. the evidence PR and exact post-evidence main five-job run succeeded;
7. Platform documents state only `Published / Consumer Candidate`, `S01-S04 complete; S05-S06 pending`.

After these checks, continue on a new CRM task branch/worktree for P09-S05 fixed-version Registry consumption. Do not claim all CRM work complete until S05 and the remaining reconciliation/final audit stages pass.
