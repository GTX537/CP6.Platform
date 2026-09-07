# P10 Formal Schema Parity and Forward Patch Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` inline, as explicitly selected by the user. Do not delegate. Track each step below.

**Goal:** Repair the shipped candidate-schema mismatch and prepare one immutable seven-package `0.10.1` publication without modifying or republishing `0.10.0`.

**Architecture:** Keep structural JSON Schema validation and semantic API validation separate, but exercise both against the same formal package identities. The reader accepts the two explicitly approved historical/patch versions; the publisher accepts only the new `0.10.1` version. Retain all signer, timestamp, version-collision, one-build, cross-platform, and protected-Environment gates.

**Tech Stack:** .NET 8, xUnit, JsonSchema.Net Draft 2020-12, PowerShell, GitHub Actions/GitHub Packages.

## Evidence and baseline

- Branch: `codex/p10-formal-schema-parity`, isolated worktree, fetched `origin/main@07e0c7e8e8ebd78f5a78904de2d7fe036b88550c`.
- CRM actual-Registry `0.10.0` regression: 12 passed, 1 failed, 0 skipped. The packaged common Schema rejects actual `BytePreserving` identities because its enum contains only `None` and `Documented`.
- CRM PR #46 is draft. Its previous 12-test Windows/Linux success does not satisfy the new regression.
- Producer focused baseline: 124 passed, 0 failed, 0 skipped. This is a local contract baseline, not publication/consumer acceptance.
- Preserve the seven historical package bytes and every S04 publication/recovery identity. No feed deletion, duplicate suppression, signing-identity change, R2 write, or deployment is in this repair branch.

## Task 1: Reproduce and repair the candidate Schema

**Files:** `tests/CP6.Platform.ReleaseTests/ReleaseSchemaParityTests.cs` (new), `ReleaseSchemaTestData.cs` (new), `P10PackageTestHarness.cs`, `P10PackageTests.cs`, and `contracts/release/v1/release-common.v1.schema.json`.

- [x] Add a shared evaluator that registers the local common Schema and evaluates the selected primary/supporting Schema without network resolution.
- [x] For both `platform.valid.json` and `system.valid.json`, pass identical formal identities to the API and Schema. Set every package's `feedTransformation` to `BytePreserving`, `timestampPolicy` to `Rfc3161Required`, and published hash equal to author-signed hash.
- [x] Add the `None`/`Documented` compatibility matrix, invalid-enum and no-timestamp negatives, all positive primary/supporting fixtures, and packaged-asset coverage. Packaged coverage must open the newly packed `.nupkg` and evaluate its own Schema bytes, not source-file substitutes.
- [x] Run `dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj -c Release --filter FullyQualifiedName~ReleaseSchemaParityTests`; record the expected `BytePreserving` Schema failure before implementation.
- [x] Apply this minimal structural repair; equal-hash enforcement remains in the semantic API:

```json
"feedTransformation": { "enum": ["None", "Documented", "BytePreserving"] }
```

```json
"allOf": [{
  "if": { "properties": { "feedTransformation": { "const": "BytePreserving" } }, "required": ["feedTransformation"] },
  "then": { "properties": { "timestampPolicy": { "const": "Rfc3161Required" } } }
}]
```

- [x] Rerun the regression and package tests. Review all discovered structural mismatches before moving on; do not assert universal semantic equivalence of JSON Schema and the API.
- [x] During S06 planning, test references to every ID in `Cp6ReleaseContractIds.All` through both the candidate API and Schema. Observe 20 passed / 4 failed: both layers reject the owned formal-publication and pinned NuGet trust media types. Add only `application/vnd.cp6.formal-package-publication.v1+json` and `application/vnd.cp6.pinned-nuget-trust-store.v1+json` to `Cp6ReleaseMediaTypes.cs` and the common Schema, and include them in the actual packaged-Schema test.
- [x] Update the exact closed media-type count in `PrimaryCandidateValidationTests.cs` from 17 to 19 after observing that assertion fail; preserve uniqueness, ordering, fixture-reference and rejected-media-type checks.

## Task 2: Pin the forward patch and retain historical readers

**Files:** `Cp6FormalPackagePublicationValidator.cs`, `FormalPackagePublicationTests.cs`, `formal-package-publication.v1.schema.json`, `CP6.Platform.Release.csproj`, `.github/workflows/p10-formal-packages.yml`, six formal package scripts, `P10FormalWorkflowContractTests.cs`, and `tests/p10/formal-package-scripts.Tests.ps1`.

- [x] Add API/Schema positive tests for all-seven `0.10.0` and all-seven `0.10.1`; negative tests for mixed sets, root/package mismatch, mismatched feed version, unapproved stable versions and prereleases. Observe `0.10.1` fail first.
- [x] Replace the validator's single constant with a checked root version; pass that root version into package/feed validation:

```csharp
var version = Cp6ReleaseJsonRules.RequireString(root, "version", "package-version");
if (version is not ("0.10.0" or "0.10.1"))
    throw Error("package-version", "Formal publication version is not approved.");
```

- [x] Update the formal publication Schema to the same two-version allowlist, with conditional package/feed version constraints so mixed identities still fail structurally.
- [x] Set the workflow, prerequisite, pack, sign and push entry points to exactly `0.10.1`: `[ValidatePattern('^0\.10\.1$')]`. Set read-only package verification and publication-record generation to `[ValidatePattern('^0\.10\.[01]$')]`. Leave the historical recovery workflow fixed to `0.10.0`.
- [x] Retarget publisher/script tests to `0.10.1` and add checks proving write entry points reject `0.10.0`; preserve reader tests for historical evidence. Keep every immutable-collision/cleanup/secret-scope failure gate.
- [x] Run the full Release test project and `pwsh -NoProfile -File tests/p10/formal-package-scripts.Tests.ps1`.
- [x] Preserve five final public records in the normal publication Artifact: formal publication, build provenance, feed read-back, Windows verification and Linux verification. A workflow regression must fail before adding the four byte-preserving copies, then pass. This lets S05/S06 verify the normal `0.10.1` path without pretending it used the historical recovery workflow.

## Task 3: Review, verify, merge and publish

**Files:** `README.md`, `CHANGELOG.md`, `VERSION`, `tests/CP6.Platform.UnitTests/FoundationContractTests.cs`, `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`, `docs/P10-RELEASE-GOVERNANCE.md`, and this plan. Cross-repository project-memory closeout follows in the already authorized P10 integration work, after real evidence exists.

- [x] Document the discovered defect, `0.10.1` pending-publication state, immutable `0.10.0` history, and continued S05/S06 No-Go.
- [x] Keep the four-part repository audit version coherent with the Release project: `VERSION=0.10.1.0`, matching changelog heading and default `0.10.1-test.local.1` test-package assertion. The first full Unit gate detected the omitted audit-version update (123 passed, 1 failed); do not bypass that check.
- [x] Synchronize the architecture documentation assertions with the same audit version and the precise historical-complete/patch-pending status. The first Contract gate detected these stale assertions (97 passed, 1 failed); preserve all seven-package, trust, evidence and non-deployable checks.
- [x] Run local Format, Build, Unit, Integration, E2E, Contract and Security gates using `eng/verify.ps1`; run P09 Compose, cleanup-failure and Kubernetes negative script checks.
- [x] Verify Docker-dependent SQL, Dapr/Kafka and P09/Kubernetes gates with the unchanged real remote jobs. The local Docker daemon is stopped; synthetic tests are not substitutes.
- [x] Review `git diff origin/main --check` and the complete diff; stage only explicitly listed task files, commit, push, open PR, and require every check to pass before merging.
- [x] Verify the exact merged main commit's full required CI and remote containment. Only then dispatch the fixed-version publication workflow with that exact main SHA and `version=0.10.1`.
- [x] Respect the protected Environment approval; never approve or bypass it on the user's behalf. Fresh preflight must establish all seven version slots are absent before the one build/sign/push/read-back run.
- [x] Record actual Windows/Linux publication evidence, hashes and immutable artifact identity before retargeting CRM PR #46. Failed or partial publication burns `0.10.1`; do not retry a consumed version.

## Self-review

This is a forward compatibility repair within P10, not a second publisher authority. Fixtures and local package tests are regression evidence only. S04 historical success remains true, but S05/S06 and overall P10 completion remain unclaimed until the corrected real package set and downstream evidence pass.

## Local execution evidence (2026-09-07)

- Observed three intended candidate-Schema failures first (both lanes and the actual newly packed Schema), then all 37 corresponding cases passed.
- Observed the two new-version API/Schema failures before the version change; all 66 focused publication/Schema/package cases then passed.
- Observed the missing-final-record workflow failure before adding the copies; all 10 workflow contract tests then passed.
- `eng/verify.ps1` reported Passed for Format, Build, Unit (124), Integration (142), E2E (31), Contract and Security. Architecture recheck passed 98; P09 Deployment contracts passed 546. All test counts have zero failures and zero skips in their successful runs.
- The full Contract gate also passed the P10 package scripts, formal scripts including historical-reader coverage, package content safety and deterministic package reproduction. The final Release recheck, including the added artifact-collection regression, passed 190/190 with zero skipped in 4 minutes 25 seconds.
- P09 Compose, cleanup-failure and Kubernetes negative script suites passed with the pinned local .NET 8 host. Docker-dependent real gates remain a remote PR/exact-main requirement.
- The complete 25-file diff preserves the historical recovery workflow, trust/certificate files and five-job validation workflow unchanged. No package archive, private signing material, credential, test output or unrelated worktree change is staged.
- PR #51 opened at `586b84e63b3cb7a51e05e3f44488ac556e90077a`; its first remote run is not the final acceptance run because the owned-contract media-type follow-up adds one source file and 24 regression cases. Require fresh head and exact-main checks for the final commit.
- The media-type follow-up also updates the existing exact-count assertion (17 to 19), bringing the complete branch to 27 files. Fresh Format and 101 focused API/Schema/primary/package tests pass, with zero failures or skips; full required gates must be rerun for this final head before merge.

## Reviewed-source and dispatch evidence (2026-09-07)

- Final repair head `06ef5aefbcca71f3b9d00a111170ab46029eae55` passed fresh local Format, Build, Unit (124), Integration (142), E2E (31), Contract (Architecture 98, Release 214, formal scripts and package reproduction), and Security gates. Successful test runs had no failures or skips.
- PR #51 final run `34123886765` passed all five unchanged remote jobs. Normal PR merge produced `main@3ff27e26962dcfd722887afb80a4306010dd9ee1`; its tree is byte-identical to the reviewed repair head. Exact-main run `34125176309` then passed all five jobs, including real SQL, Dapr/Kafka and P09 runtime checks.
- Before publication, the owner explicitly approved protecting Platform `main`. Read-back confirmed strict required status checks for the same five GitHub Actions contexts, administrator enforcement, zero required approving reviews, and force-push/deletion disabled. The audit is recorded on PR #51. This protects the new source; it does not retroactively protect the historical `0.10.0` source.
- Read-only Registry inspection confirmed that all seven `0.10.1` version slots were absent. The protected publishing job must independently repeat its full prerequisite gate; this preliminary check is not a substitute.
- Dispatched `.github/workflows/p10-formal-packages.yml` once with exact source `3ff27e26962dcfd722887afb80a4306010dd9ee1` and version `0.10.1`. Its Git blob is `01c94e11213231ef6f73d8f9a9cccd9f1563e185`; run `34126521193`, attempt `1`, began at `2026-09-07T13:16:59Z`. The owner approved the existing `p10-formal-release` Environment. At dispatch time, publication and two-OS acceptance remained pending; the later completed result is recorded below.
- Publication evidence is being recorded on `codex/p10-s04-patch-publication-evidence`, created from that verified exact main in the existing clean isolated worktree. The P10 documentation baseline test passed before edits. No application, publisher, signer or historical package bytes are changed by this evidence branch.

## Corrective publication result

- Run `34126521193`, attempt `1`, completed with `success`; both Windows `sign-publish` and `verify-linux` succeeded, including public scans and private cleanup. Version `0.10.1` is now consumed and must never be re-run or republished.
- Final artifact `10020487845` matched archive digest `sha256:27c1a3178ce694f0ba3f8be6b42d505d0c564fc1cb9a4772387f41ea20c85e33`; Windows read-back artifact `10020464999` matched `sha256:00c8732f6fbbe3dbcf0df9e5074882837ca14ef48e4a0d31f67f524301e11e1d`. Both expire at `2026-12-06T13:17:00Z`.
- Independent local Current-mode verification passed all seven downloaded package subjects and the canonical publication record (`8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961`). The governance ledger records the original five record hashes, seven package hashes, real timestamp signer and source-protection audit.
- The new completed-publication documentation assertion failed against the previous pending status before the ledgers were updated. This evidence-only branch still requires its own full diff review, required checks, normal PR merge and exact-main verification before downstream consumers bind its evidence-main SHA.
- S05/S06 and the overall P10 final decision remain pending; publication does not imply consumer acceptance or deployment authority.
