# P09 Deployment package publication prerequisite

| Field | Value |
| --- | --- |
| Publication status | Not published |
| Stage | `P09-S04: Not started` |
| Candidate package | `CP6.Platform.Deployment 0.9.0-alpha.1` |
| Source requirement | exact `origin/main` after the implementation PR and exact-main validation pass |

Publication status: Not published.

This is a publication prerequisite and operator runbook. It records what a future separately authorized P09-S04 task must prove; it is not evidence that a package upload, Registry mutation, CRM consumption, or environment rollout has occurred.

## Immutable scope

S04 may publish `CP6.Platform.Deployment` at exact version `0.9.0-alpha.1`, and only this package. The existing five P08 runtime packages remain immutable at `0.8.0-alpha.2`; `CP6.Platform.Testing` and `CP6.Platform.P09Fixture` remain repository-only.

Use one authoritative Registry. The current Platform package source is GitHub Packages; any proposal to change that authority requires an explicit migration and rollback decision before S04. Never build the same version independently for two registries or treat mutable tags as package identity.

## Preconditions

1. Fetch the remote and require a clean working tree at exact `origin/main`.
2. Require the implementation PR merge commit to be the selected `origin/main` commit.
3. Require the exact-main `platform-validation` run to pass the Windows and Linux matrix, `ubuntu-dapr-kafka`, `ubuntu-sql-server`, and `ubuntu-p09-non-production-runtime`.
4. Confirm no `CP6.Platform.Deployment 0.9.0-alpha.1` version already exists. A collision must reject overwrite; do not skip, replace, delete, or mutate an existing version.
5. Use a short-lived package credential only inside the protected publication job. Do not persist it in files, logs, artifacts, repository settings, or developer machines.

## Required pre-publication gates

The future S04 workflow must rerun the established P05, P06, P08, and P09 boundaries from the selected source commit:

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile ci
pwsh ./eng/verify.ps1 -Gate Build -Profile ci
pwsh ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh ./eng/verify.ps1 -Gate Integration -Profile p05-real
pwsh ./eng/verify.ps1 -Gate Integration -Profile p06-real
pwsh ./eng/verify.ps1 -Gate E2E -Profile ci
pwsh ./eng/verify.ps1 -Gate Contract -Profile ci
pwsh ./eng/verify.ps1 -Gate Security -Profile ci
pwsh ./eng/verify.ps1 -P09Real -Profile ci -ExpectedGitSha <exact-origin-main-sha>
pwsh ./eng/pack-p09.ps1 -VerifyReproducible
```

No required failure may be converted to a warning, skipped result, duplicate-ignore option, or manual assertion.

## Candidate manifest

Before upload, produce and retain a machine-readable candidate manifest containing:

- exact source Git SHA and workflow run/job identity;
- package ID/version and the ordinary package SHA-256 (`package SHA-256`);
- symbol package identity if retained as evidence;
- Profile, Compose, Kubernetes manifest, and rehearsal `evidence SHA-256` values;
- resolved Dapr, Kafka, and kubectl image digests from the exact rehearsal;
- gate summary paths and conclusions;
- Registry authority and target package URL, without credentials.

The ordinary package must contain the non-empty `lib/net8.0/CP6.Platform.Deployment.dll`, its XML documentation, all and only approved `contracts/p09/**` and `deploy/p09/**` assets, a dependency-free nuspec, no test/build output, no Evidence artifact, no local secret-store file, no kubeconfig, no machine path, and no secret-like value.

## Publication transaction

The future workflow must build once from the exact selected commit, verify the produced bytes, upload that exact ordinary package once, and then query the Registry independently. It must not rebuild between verification and upload. Symbols may be retained as evidence according to the existing Platform policy but are not a substitute for the ordinary package identity.

After upload, independently download the immutable version and require its SHA-256 and package contents to match the pre-upload manifest. Retain the workflow artifact ID/digest, package SHA-256, Profile and manifest hashes, rehearsal evidence SHA-256, and Registry version identity.

If upload status is uncertain, query the Registry before retrying. If bytes were accepted, do not retry an overwrite. If the accepted artifact is later disqualified, preserve it as historical evidence and use a separately approved forward-only version; never delete or rewrite history.

## Post-publication boundary

A successful S04 result advances only to the publication candidate boundary. S05 must then consume the fixed version from the Registry in CRM-owned black-box tests without project references or copied source. S06 must later synchronize the public repository and perform the final Platform evidence audit. Neither step is part of this runbook execution.

This file authorizes no cloud resource, Kubernetes context, environment deployment, CRM runtime registration, business Topic, gateway route, worker process, production approval, or package upload in the current S01-S03 task.
