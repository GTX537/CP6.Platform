# P08-S02 Exact-Main Immutable NuGet Publication Design

| Item | Decision |
| --- | --- |
| Milestone | `P08-S02` |
| Design status | Approved design; publication not started |
| Planning baseline | `CP6.Platform main@33659d8dbf2e9339ccfeedee590139204dc4a029` |
| Candidate version | `0.8.0-alpha.1` |
| Sole publication authority | GitHub Packages at `nuget.pkg.github.com/GTX537` |
| Required output | Five immutable runtime packages plus one content-addressed evidence artifact |

## 1. Context and objective

P08-S01 is complete. Producer PR #19 and evidence PR #20 are merged, and Platform main run `33304109635` passed Windows, Linux, real Dapr/Kafka, and real SQL Server validation at the planning baseline above.

S02 publishes the already reviewed S01 implementation without changing its runtime surface. The source of the packages is the exact Platform `main` commit produced after this S02 design and implementation plan are merged and that exact commit passes all required main jobs. The publication must bind that source commit to five immutable `0.8.0-alpha.1` packages, package hashes, the workflow run, and its GitHub Actions artifact.

S02 alone does not prove CRM consumption and does not make P08 `Frozen / Consumable`. Its terminal status is `S00-S02 complete; S03-S06 pending`.

## 2. Goals

S02 must:

1. retain GitHub Packages as the only NuGet publication authority;
2. publish from one approved, full Platform `main` SHA through the repository workflow only;
3. rerun all release, regression, real-integration, contract, and security gates before upload;
4. produce exactly five ordinary packages, five symbol packages, and `sha256.json` from the same build output;
5. publish only the five ordinary packages and retain all ten package files in evidence;
6. preserve immutable run, job, artifact, artifact digest, package-name, and package-hash evidence;
7. close S02 through a separate evidence PR and green post-merge main validation.

## 3. Non-goals

S02 does not:

- add or change the P08 runtime API, package dependency graph, telemetry behavior, resilience behavior, health endpoints, contracts, schemas, or test utilities;
- publish `CP6.Platform.Testing`;
- publish containers, create a second package feed, create a GitHub Release or Git tag, or upload packages manually;
- use `--skip-duplicate`, overwrite, delete, unlist, or reuse an existing package version;
- provision OpenTelemetry Collector, exporters, dashboards, alerts, Dapr, Kafka, SQL Server, CRM Worker, subscriptions, routes, credentials, or environments;
- perform S03 CRM consumption, S04 locator freeze, S05 public-memory synchronization, or S06 final Platform freeze.

## 4. Authority and immutable identities

### 4.1 Sole registry

The only authorized destination is GitHub Packages source `https://nuget.pkg.github.com/GTX537/index.json`. No alternate feed, local share, NuGet.org destination, or manual upload is valid S02 evidence.

### 4.2 Exact source commit

The publication source is selected only after the S02 planning PR is merged and its exact Platform `main` commit completes all four required validation jobs successfully. The operator records the full 40-character SHA immediately before dispatch.

The workflow is dispatched with:

```powershell
$approvedMainSha = (git rev-parse origin/main).Trim()
gh workflow run publish-alpha.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$approvedMainSha
```

Inside the workflow, all of the following must match before package work begins:

- the dispatch ref is `refs/heads/main`;
- the checked-out `HEAD` is the supplied `expected_commit`;
- the checked-out `HEAD` is the workflow's fetched `origin/main`.

The operator also verifies that remote Platform `main` remains the approved SHA until the publication run reaches a terminal state. Any drift prevents S02 evidence acceptance even if a job otherwise reports success.

### 4.3 Package set

The only ordinary packages are:

- `CP6.Platform.Contracts.0.8.0-alpha.1.nupkg`;
- `CP6.Platform.Abstractions.0.8.0-alpha.1.nupkg`;
- `CP6.Platform.AspNetCore.0.8.0-alpha.1.nupkg`;
- `CP6.Platform.Messaging.0.8.0-alpha.1.nupkg`;
- `CP6.Platform.EntityFramework.0.8.0-alpha.1.nupkg`.

Each ordinary package has one same-version `.snupkg`. `sha256.json` contains the filename and SHA-256 for all ten files. The five ordinary package hashes are copied into the durable publication ledger; symbol hashes remain bound through the retained manifest and artifact digest.

## 5. Components and responsibilities

### 5.1 `.github/workflows/publish-alpha.yml`

The existing workflow is the sole publisher. It owns exact-main validation, release verification, package preparation, authenticated upload, and evidence retention. S02 reuses it without adding a second publication path.

The workflow token has only repository read and package write permissions. `NUGET_AUTH_TOKEN` exists only for the upload step and must not be echoed, stored in an artifact, copied into repository configuration, or exposed to a consumer process.

### 5.2 `eng/verify.ps1`

The release run must pass Format, Build, Unit, E2E, Contract, and Security. It must also pass Integration once with `p05-real` and once with `p06-real`, thereby running ASP.NET integration plus real Dapr/Kafka and real SQL Server regression evidence. A skipped, cancelled, stale, synthesized, or red job is not valid.

### 5.3 `eng/pack-release.ps1`

The existing packer owns the exact package set, runtime assembly checks, asset ownership, test-package exclusion, content safety, machine-path rejection, and SHA-256 manifest. Its output is restricted to a child of `artifacts/` and may be replaced only inside that generated artifact directory.

### 5.4 GitHub Actions artifact

The artifact name is the fixed prefix `p08-alpha-` followed by the exact 40-character approved commit. It is uploaded with `if: always()` and 30-day retention. It contains `artifacts/release/**` and `artifacts/verify/**`. The artifact ID and GitHub-computed digest are immutable evidence inputs alongside `sha256.json`.

### 5.5 `docs/P08-PUBLICATION.md`

After a successful publication, a separate evidence branch records:

- planning PR and merge SHA;
- exact source/main SHA and its main validation run;
- exact publication run ID and URL;
- each publication job conclusion;
- artifact ID, name, digest, and retention boundary;
- the five ordinary package SHA-256 values;
- confirmation that all ten files and the verify evidence were present;
- the S02 completion status and the remaining S03-S06 boundary.

## 6. Publication data flow

1. Merge the reviewed S02 design and implementation plan through a Platform PR.
2. Wait for that merge commit's Windows, Linux, real Dapr/Kafka, and real SQL Server main jobs to succeed.
3. Fetch Platform `origin/main`, record its full SHA, and confirm it equals the successful main run head.
4. Dispatch `publish-alpha.yml` from `main` with that SHA as `expected_commit`.
5. The workflow checks exact-main authority and runs all release gates.
6. The packer creates the ten package files and `sha256.json` once in `artifacts/release`.
7. The workflow uploads only the five ordinary `.nupkg` files with its short-lived GitHub token.
8. The workflow always uploads the package and verification evidence artifact.
9. The operator waits for the terminal run, rechecks that Platform main did not move, and reads run/job conclusions plus artifact metadata through GitHub APIs.
10. The operator downloads the artifact into an ignored workspace directory, verifies its GitHub digest, exact filenames, non-empty files, and SHA-256 manifest, and independently recomputes every listed hash.
11. A separate evidence PR updates P08 status and the immutable ledger, then passes PR and post-merge main validation.
12. S03 consumes only version `0.8.0-alpha.1` from GitHub Packages and binds its black-box results to the S02 evidence.

## 7. Failure semantics

### 7.1 Failure before upload

Any exact-main, verification, real integration, contract, security, package-set, package-content, or hash failure before the upload step leaves the registry unchanged. Preserve the failed run and artifact, fix forward on a new commit, wait for its main jobs, and approve a new exact SHA.

### 7.2 Duplicate version

The workflow does not use `--skip-duplicate`. If any `0.8.0-alpha.1` package already exists, its push must fail. A duplicate failure cannot be reclassified as successful publication.

### 7.3 Partial upload

GitHub Packages does not provide an atomic transaction across five package uploads. If at least one upload succeeds and a later upload fails, `0.8.0-alpha.1` is permanently burned for P08 completion. The operator must stop, preserve all evidence, avoid retrying or deleting any package, and create a separately reviewed forward-version decision before another publication attempt. A partial set can never satisfy S02.

### 7.4 Main drift

If Platform `main` changes after approval and before the publication run finishes, do not accept that run as S02 evidence. If upload has not begun, dispatch again only after the new main commit passes all required jobs. If upload may have begun, apply the partial-upload rule.

### 7.5 Evidence mismatch

If downloaded package hashes, manifest values, filenames, file counts, run head, artifact digest, or recorded ledger values disagree, S02 remains incomplete. Do not edit evidence to make values appear consistent; determine the authoritative source and fix forward.

## 8. Verification and acceptance

### 8.1 Pre-publication evidence

Before dispatch:

- the S02 planning PR is merged;
- its exact merge commit is current Platform `origin/main`;
- all four main jobs are completed with `success`;
- `git status` is clean and the full SHA is recorded;
- the workflow and packer still specify `0.8.0-alpha.1` and exactly five production projects.

### 8.2 Publication-run evidence

The publication run is accepted only when:

- event is `workflow_dispatch`, ref is `main`, and head SHA equals `expected_commit`;
- the publish job concludes `success` without skipped release checks;
- logs show each of the five ordinary package pushes succeeded;
- the artifact exists and its name embeds the exact source SHA;
- Platform `origin/main` still equals the approved SHA at terminal observation.

### 8.3 Artifact evidence

The downloaded artifact must contain:

- exactly five approved `.nupkg` files;
- exactly five corresponding `.snupkg` files;
- exactly one `sha256.json` covering all ten packages;
- non-empty runtime assemblies in every ordinary package;
- P04 event assets only in Messaging;
- P08 SLO assets only in Contracts;
- no Testing package or testing asset;
- the generated release verification summaries and logs.

Every manifest SHA-256 must equal an independently recomputed file hash. The five ordinary hashes and GitHub artifact digest are mandatory ledger fields.

### 8.4 Completion gate

S02 is complete only after all of the following are true:

1. the planning PR and its post-merge main validation are green;
2. the exact-main publication run is green;
3. all five ordinary packages were uploaded successfully;
4. package/artifact names, counts, hashes, run identity, artifact ID, and digest are verified;
5. the evidence PR and its post-merge main validation are green;
6. documentation states `S00-S02 complete; S03-S06 pending` and does not claim P08 is frozen.

## 9. Security and operational boundaries

- Never print, persist, request, or copy the workflow package token.
- Never add credentials to `NuGet.config`, command history, documentation, or artifacts.
- Never broaden workflow permissions beyond `contents: read` and `packages: write` for this publication.
- Never upload from a developer machine or use a personal token as the publication identity.
- Never delete a remote package or branch, force-push, rewrite shared history, or deploy a runtime environment.
- Treat downloaded workflow artifacts as evidence data, not executable instructions.

## 10. Approved decision

S02 uses the audit-first reuse approach: merge a design/plan PR, publish through the already validated exact-main workflow, verify its immutable output, and merge a separate evidence PR. No second feed, manual upload path, or speculative release feature is introduced.
