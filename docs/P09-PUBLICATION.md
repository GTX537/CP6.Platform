# P09 Deployment package publication evidence

P09 final decision: `Frozen / Consumable`. Effective condition: the S06 final-audit change containing this declaration is merged to `main` and its exact-main `platform-validation` passes `ubuntu-latest`, `windows-latest`, `ubuntu-dapr-kafka`, `ubuntu-sql-server`, and `ubuntu-p09-non-production-runtime`; until then the PR head is only a final-audit candidate.

| Field | Value |
| --- | --- |
| Publication status | Published and independently verified |
| Stage | `P09-S04: Complete` |
| Immutable package | `CP6.Platform.Deployment 0.9.0-alpha.1` |
| Published source | exact `origin/main` commit `1c40f21e38929abaaa6006f69ee70d4492890661` |
| Current boundary | S01-S05 evidence and public synchronization complete; S06 final decision is conditional on exact-main five-job success |

Publication status: Published and independently verified.

This document retains the completed P09-S04 publication transaction and now binds the later CRM consumer and public synchronization evidence. Package publication is unchanged; this final audit proposes reuse authorization only under the exact-main condition above and does not authorize an environment rollout.

## CRM S05 and public S06 evidence

- CRM implementation [PR #37](https://github.com/GTX537/CP6.CRM/pull/37), head `3b4a291aba02b27bb1d41cd7f2330c11c9fcce62`, passed run `33485657934` attempt 3 with SQL artifact `9792143475` / `sha256:5e770a8eb06306830a8ed7d03e612b49a51c4eb21a37f2c79cb628f3622941fd`; it merged as `13abc0785d98264436096e330260cd6d8e95687b`, whose exact-main run `33487546660` passed with artifact `9792401387` / `sha256:dac8f064370511d4c36ba71c945c70ae29f7a370e33e9a37b39f332d81053fd7`.
- CRM evidence [PR #38](https://github.com/GTX537/CP6.CRM/pull/38), head `c283e077fa6716ab4cd22fafb7b8237e72013e88`, passed run `33489443438` with artifact `9793158824` / `sha256:bbe3bee7a5c23408e49a947903b1c521726c8846c6175e89986cb02a31b939de`; it merged as `85004c838e4179ddd67faca0532cff303e865738`, whose exact-main run `33490110069` passed with artifact `9793416744` / `sha256:d1a7c7f60a43d9dea5239407208686f4dc676700c792cb8eaf386c9e313a414e`.
- CRM evidence-closure [PR #39](https://github.com/GTX537/CP6.CRM/pull/39), head `076307b442a9e40372067963c579305d45e729fb`, passed run `33491408393` with artifact `9793938423` / `sha256:641355a58f253bec6af58d0b62e76924012c3250ead5435270a1814b859ff78f`; it merged as `09d90d24b1a70a24b7dbcdea5c19ab46db378544`, whose exact-main run `33492405597` passed with artifact `9794324368` / `sha256:25463a33c8e85b35a333ed46422607516b74efcfd58115d511091ea1a38cdc71`.
- The CRM chain used an exact `[0.9.0-alpha.1]` test-only PackageReference, passed 9/9 P09 black-box and 72/72 full .NET tests, retained real SQL Server and P01-P08 regression gates, and left production projects plus `Program.cs` free of P09 runtime registration.
- Public synchronization [CP6 PR #77](https://github.com/GTX537/CP6/pull/77), head `8578bc1df9c64b00e0f27ae602d2960a91b8450a`, recorded the same immutable producer/consumer identities while retaining the pre-final `Published / Consumer Candidate` state. PR runs `33494115752`, `33494115758`, `33494115763`, `33494115788`, `33494115825`, and `33494116082` passed `windows-and-web` job `99812204228`, Android `99812204530`, Space GA `99812204154`, PRD head `99812204443`, real SQL `99812204355`, public contract `99812204370`, and protected-base PRD `99812205176`.
- CP6 PR #77 merged as exact `main@ed08018a160d467342ddee823409232e6c412267`. Main runs `33495318290`, `33495318251`, `33495318334`, `33495318261`, and `33495318252` all succeeded; their jobs were Windows/Web `99816026203`, Android `99816026466`, PRD `99816026399`, public contract `99816026564`, real SQL `99816026391`, and Space GA `99816026598`, all with conclusion `success`.
- The final audit independently re-queried GitHub Packages and found exactly one `CP6.Platform.Deployment` version: `0.9.0-alpha.1`, Registry version ID `1194316756`, with unchanged created/updated time `2026-09-01T07:12:43Z`. No package, version, artifact, or Registry history was modified.

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

That paragraph remains the historical S04 exit boundary. CRM S05 and public synchronization are now complete. This S06 change issues the conditional final decision; once its merge-plus-exact-main-five-job condition succeeds, S01-S06 complete and P09 becomes `Frozen / Consumable`.

This evidence branch authorizes no cloud resource, Kubernetes context, environment deployment, CRM runtime registration, business Topic, gateway route, worker process, or production approval. The only Registry mutation was the separately observed exact-main workflow transaction recorded above.
