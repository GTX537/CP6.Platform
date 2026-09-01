# P09 Deployment package publication evidence

| Field | Value |
| --- | --- |
| Publication status | Published and independently verified |
| Stage | `P09-S04: Complete` |
| Immutable package | `CP6.Platform.Deployment 0.9.0-alpha.1` |
| Published source | exact `origin/main` commit `1c40f21e38929abaaa6006f69ee70d4492890661` |
| Current boundary | `S01-S04 complete; S05-S06 pending` |

Publication status: Published and independently verified.

This document records the completed P09-S04 exact-main publication and retains the operator controls that produced it. Publication is proven; CRM consumption, public synchronization, environment rollout, and final reuse authorization are not.

## Implemented publication transaction

`.github/workflows/publish-p09.yml` is the sole P09 publisher. It is manual-dispatch only, requires an exact current `main` commit, reruns all required P05/P06/P08/P09 gates, invokes `eng/pack-p09.ps1 -VerifyReproducible`, and publishes one explicit ordinary package path. It does not modify the historical P08 publisher.

Before mutation, `eng/p09/Test-P09RegistryPackage.ps1 -Mode Available` rejects an existing `0.9.0-alpha.1` version and `eng/p09/New-P09PublicationManifest.ps1` records the exact package, source, gate, rehearsal, manifest, and image identities. After the single push, `Test-P09RegistryPackage.ps1 -Mode Published` independently queries the Registry, downloads the accepted fixed version, verifies its SHA-256 and content boundary, and writes a non-secret Registry result.

Every outcome preserves package, publication, verification, P05, P06, rehearsal, and Kubernetes roots in one 30-day workflow artifact. If upload status is uncertain, operators must query the Registry and preserve the run before taking any further action. The workflow never retries an overwrite and contains no duplicate-ignore option.

## Completed publication evidence

| Evidence | Verified value |
| --- | --- |
| Implementation PR | [PR #29](https://github.com/GTX537/CP6.Platform/pull/29), head `914e70cae4d78114ae181515cfaece11a62a28be` |
| Successful PR validation | run `33479175077`, five jobs passed |
| Merge commit | `1c40f21e38929abaaa6006f69ee70d4492890661` |
| Successful exact-main validation | run `33479705779`, five jobs passed |
| Publication workflow | run `33480300468`, job `99768201448`, success, `2026-09-01T07:03:06Z` through `2026-09-01T07:12:57Z` |
| Retained artifact | ID `9789925866`, `p09-publication-1c40f21e38929abaaa6006f69ee70d4492890661`, 730706 bytes |
| Artifact digest | `sha256:3daad67d4a15144d5f22b64637f7e9f91bdedc4e95fec4e5e20dd09977d78f27` |
| Registry version | ID `1194316756`, created and updated `2026-09-01T07:12:43Z` |
| Ordinary package | `CP6.Platform.Deployment.0.9.0-alpha.1.nupkg`, 64786 bytes, SHA-256 `e820d1771ed004b4a7089d008eef3bb2aca4fe35e4912d67057840373c4952cb` |
| Symbols retained as evidence | `CP6.Platform.Deployment.0.9.0-alpha.1.snupkg`, 16410 bytes, SHA-256 `6927cd175f61da8bfff5211f5fd025da32af33b6db388858180aa0e2148c94be`, `EvidenceOnly` |
| Profile identity | `cp6-platform-p09-ci-v1`, SHA-256 `94addf0349ff895f21eca3e0d660c8d5159198267080df9109ff6493c1063681` |
| Compose manifest | SHA-256 `4087f06c01f7b5530542b146a92a9c8695c67ae6d6b2c1e2112b224fa4a32caa` |
| Kubernetes manifest | SHA-256 `0551f469e6e8ad0de52b48a10551d6a3f6c66bdef5c273ac29a757d36df44fc5` |
| Rehearsal evidence | SHA-256 `2ffb1365e3d0cb85970e7bc148271bdc4b2ca0b37e5dbb55f772fc4f37d4bf5d`, 12 checks passed, zero residue |
| Resolved images | Dapr `sha256:8e94ba37d6bc95875e88545ce7d8ff781354f29080db02706d57446285fcc4a5`; Kafka `sha256:77e3df9054047a88b520d0cc46e16696d3b22022e1d580aeccd2632df6532837`; kubectl `sha256:59bafa07ff3a6d4b417e7633ddb9d79a9606ca98bf64bac080b3e65748669250` |

The initial and immediately pre-push availability checks both returned `Available`. The workflow uploaded one explicit ordinary package, then downloaded that fixed Registry version. A separate post-run download of artifact `9789925866` recomputed the candidate and Registry-download hashes as the same `e820d1771ed004b4a7089d008eef3bb2aca4fe35e4912d67057840373c4952cb`, inspected the required DLL, contracts, Compose, and Kubernetes entries, and independently queried exactly one Registry version ID `1194316756`. P05 real Dapr/Kafka, P06 real SQL Server, all 11 candidate-manifest gates, both Kubernetes results, and the P09 zero-residue boundary were `Passed`.

## Immutable scope

S04 published `CP6.Platform.Deployment` at exact version `0.9.0-alpha.1`, and only this package. The existing five P08 runtime packages remain immutable at `0.8.0-alpha.2`; `CP6.Platform.Testing` and `CP6.Platform.P09Fixture` remain repository-only.

Use one authoritative Registry. The current Platform package source is GitHub Packages; any proposal to change that authority requires an explicit migration and rollback decision before S04. Never build the same version independently for two registries or treat mutable tags as package identity.

## Preconditions

1. Fetch the remote and require a clean working tree at exact `origin/main`.
2. Require the implementation PR merge commit to be the selected `origin/main` commit.
3. Require the exact-main `platform-validation` run to pass the Windows and Linux matrix, `ubuntu-dapr-kafka`, `ubuntu-sql-server`, and `ubuntu-p09-non-production-runtime`.
4. Confirm no `CP6.Platform.Deployment 0.9.0-alpha.1` version already exists. A collision must reject overwrite; do not skip, replace, delete, or mutate an existing version.
5. Use a short-lived package credential only inside the protected publication job. Do not persist it in files, logs, artifacts, repository settings, or developer machines.

## Required pre-publication gates

The S04 workflow reruns the established P05, P06, P08, and P09 boundaries from the selected source commit:

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

The workflow builds once from the exact selected commit, verifies the produced bytes, uploads that exact ordinary package once, and then queries the Registry independently. It does not rebuild between verification and upload. Symbols may be retained as evidence according to the existing Platform policy but are not a substitute for the ordinary package identity.

After upload, independently download the immutable version and require its SHA-256 and package contents to match the pre-upload manifest. Retain the workflow artifact ID/digest, package SHA-256, Profile and manifest hashes, rehearsal evidence SHA-256, and Registry version identity.

If upload status is uncertain, query the Registry before retrying. If bytes were accepted, do not retry an overwrite. If the accepted artifact is later disqualified, preserve it as historical evidence and use a separately approved forward-only version; never delete or rewrite history.

## Post-publication boundary

A successful S04 result advances only to `Published / Consumer Candidate`. S05 must now consume the fixed version from the Registry in CRM-owned black-box tests without project references or copied source. S06 must later synchronize the public repository and perform the final Platform evidence audit. `S01-S04 complete; S05-S06 pending`.

This evidence branch authorizes no cloud resource, Kubernetes context, environment deployment, CRM runtime registration, business Topic, gateway route, worker process, or production approval. The only Registry mutation was the separately observed exact-main workflow transaction recorded above.
