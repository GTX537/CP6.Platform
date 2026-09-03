# P10 Release Governance

## Status and authority

P10 Platform status: **S04 complete**.

P10 overall: **Candidate / No-Go pending S05 and S06**.

The exact seven-package Platform set at version `0.10.0` was built once, signed
with the pinned self-signed release identity, RFC 3161 timestamped, published to
GitHub Packages, read back byte-for-byte, and independently verified on Windows
and Linux. The immutable publication and recovery evidence is recorded below.

The current authority boundary is:

- formal Platform package publication: complete for exactly `0.10.0`;
- System candidate publication: not authorized pending S05 and S06;
- Portal fabrication: not authorized;
- R2 candidate or Locator publication: not authorized; and
- deployment: not authorized.

S04 evidence is not production authorization. S05 consumer reconciliation and
S06 cross-repository evidence, final decision, and any separately approved
deployment request remain mandatory.

## Package ownership and exact package set

`CP6.Platform.Release` owns release schemas, deterministic JSON bytes, semantic validators, trust policy models, and the repository-local validation CLI. It is independent of the other runtime packages and does not add a runtime dependency edge.

S02 creates one exact version of seven ordinary packages and seven matching symbol packages:

| Package ID | P10 role |
| --- | --- |
| `CP6.Platform.Abstractions` | Existing Platform abstractions in the test package set |
| `CP6.Platform.AspNetCore` | Existing ASP.NET Core integration in the test package set |
| `CP6.Platform.Contracts` | Existing shared contracts in the test package set |
| `CP6.Platform.Deployment` | Existing non-production deployment contracts in the test package set |
| `CP6.Platform.EntityFramework` | Existing EF integration in the test package set |
| `CP6.Platform.Messaging` | Existing messaging integration in the test package set |
| `CP6.Platform.Release` | P10 release contracts and validators |

`CP6.Platform.Testing` remains repository-only and is never included. The test version is `0.10.0-test.<first-12-source-sha>.<run-attempt>`. Every manifest entry binds the same source SHA, build invocation ID, version, test certificate fingerprint, ordinary package hash, and symbol package hash.

This set is transported only by GitHub Actions artifacts. It is not written to GitHub Packages or any other NuGet feed.

## Contract groups

P10 defines four primary candidate contracts. Unknown properties fail validation.

| Primary contract | Lane and purpose |
| --- | --- |
| `system-release-manifest.v1` | Production-only four-repository compatibility root; requires `candidateKind=System` and `deployable=true` |
| `candidate-result.v2` | Result envelope for a completed real System candidate and its release gate |
| `candidate-locator.v1` | Signed discovery root with a discriminated System or Platform subject |
| `platform-release-candidate.v1` | Platform reference lane; requires `candidateKind=PlatformReference` and `deployable=false` |

The supporting evidence and policy contracts are:

- `release-gate-result.v1`: completed workflow inputs, gate conclusions, and overall result;
- `system-lineage-bootstrap-evidence.v1`: separately authorized first-System lineage evidence;
- `evidence-record.v1`: producer, access class, object reference, subjects, policy, and conclusion;
- `build-invocation-provenance.v1`: one source/build identity, seven pre-sign outputs, and seven final package subjects;
- `test-package-transport.v1`: exact S02 run plus package artifact ID, digest, creation time, and expiry; and
- `pinned-trust-store.v1`: pinned storage authority and trusted-key history;
- `pinned-nuget-trust-store.v1`: the Current and Revoked formal NuGet signer history; and
- `formal-package-publication.v1`: immutable package/feed identity, byte-preserving read-back, trust, timestamp, workflow, and two-OS verification evidence.

All schemas and `assets.v1.json` live under `contracts/release/v1/` and are packed only by `CP6.Platform.Release`.

## Deterministic byte profile

`cp6-deterministic-json-v1` signs and hashes exact canonical UTF-8 bytes. Canonicalization enforces:

- one JSON object at the root, strict UTF-8, no BOM, comments, trailing commas, or duplicate properties;
- NFC-normalized property names and string values with valid Unicode scalar pairs;
- ordinal property ordering, preserved array ordering, minimal JSON escaping, and no trailing newline;
- only non-negative base-10 integers in the Int64 range; and
- byte-identical hashing without parse-and-reserialize behavior at verification time.

Fail-closed limits are 4 MiB per JSON document, depth 32, 256 members per object, 4,096 entries per array, and 65,536 UTF-8 bytes per string. `CP6.Platform.ReleaseTool canonicalize` is the only repository script boundary used to produce final contract bytes.

## Pinned R2 storage authority

`pinned-trust-store.v1` retains one complete Cloudflare R2 authority instead of accepting storage coordinates from a Locator. Its authority ID is `cp6-release-r2-v1`, provider is `cloudflare-r2`, jurisdiction is `default`, bucket is `cp6-release`, and endpoint is derived only from the pinned account ID through `https://{accountId}.r2.cloudflarestorage.com`. The only accepted object prefixes are `candidates/platform/` and `objects/sha256/`; the access mode is `AuthenticatedReadConditionalCreate` and the per-object ceiling is 4 MiB. Repository fixtures use a non-production all-`1` account ID. The later reviewed public trust-policy instance must supply the real public account ID.

Consumers use a permanent credential limited to authenticated, read-only access to `cp6-release`. Publication uses a short-lived R2 session credential narrowed to the two approved prefixes and performs a conditional `PutObject` with `If-None-Match: *`; normal publication has no list, delete, copy, or multipart permission. The parent bucket-scoped write identity remains a protected Environment secret and is not represented in any public trust document.

This earlier contract correction itself created no credential, signing key,
candidate, Locator, bundle, or R2 object. Its historical scope does not override
the completed S04 package-publication evidence recorded below.

## Pinned cosign public-key identity

Each `pinned-trust-store.v1` key now carries one canonical cosign-compatible
PKIX `PUBLIC KEY` PEM value. The initial P10 trust domain accepts ECDSA P-256,
uses LF separators, and omits a trailing newline. The semantic validator parses
that PEM, exports its DER SubjectPublicKeyInfo, and requires `keyId` to equal
`sha256:<lowercase SHA-256 of DER SubjectPublicKeyInfo>`. A noncanonical PEM,
unsupported curve, malformed key, or mismatched digest fails with `trust-key`.

The JSON representation escapes PEM line feeds according to
`cp6-deterministic-json-v1`; consumers use the parsed string as the cosign
public-key file. This makes the reviewed trust entry and the key used by
`cosign verify-blob` the same public identity. The contract change itself did
not generate keys; the later external prerequisite bootstrap and its evidence
are recorded below.

`CP6.Platform.ReleaseTool validate-trust <path>` exposes that complete parser at
the process boundary and prints only the canonical policy SHA-256 on success.
It returns the normal contract failure code for an invalid key, storage
authority, policy version, or noncanonical document, so downstream bootstrap
does not need to duplicate or bypass the library validator.

## System and Platform lane separation

The System lane is production-only. It requires exact identities for `CP6`, `CP6.Platform`, `CP6.CRM`, and `CP6.Portal`, plus compatible packages, images, schemas, migrations, evidence, trust policy, release gates, and lineage. P10 implements rejection and positive-shape validation, but S00–S02 publish no successful System instance.

The Platform lane can describe only a Platform reference candidate. It cannot be substituted for a System manifest or result, cannot contain fabricated repository identities, and remains `deployable=false`. Locator subject kind, media type, object key, hash, creation time, trust-policy version, and signer key are bound together and validated through lane-specific entry points.

## Test certificate and artifact boundary

The S02 certificate is created at run time with subject `CN=CP6 Platform P10 TEST ONLY`, RSA-2048, SHA-256, digital-signature key usage, code-signing EKU, and a 91-day validity window. Its random PFX password exists only in process memory. Verification never modifies an operating-system certificate store. The Release tool uses the official NuGet package-verification API: archive integrity and CMS validity remain mandatory, the exact SHA-256 signer fingerprint is the only certificate permitted to chain to an untrusted root, and a separate author allow-list pins that same fingerprint. The PFX, password state, and private-key object are removed in `finally` paths; only the public certificate and fingerprint enter the test artifact.

Only the public CER, lowercase SHA-256 fingerprint, 14 signed package files, canonical manifests/evidence, locked-restore metadata, sanitized gate summaries, and `sha256.json` enter the package artifact. The package artifact is independently verified before upload. A second artifact contains only `test-package-transport.v1.json` and binds the first artifact's API ID and digest.

Both artifacts are non-overwritable and have artifact retention of 90 days. Expired artifacts cannot be used by S03; S02 must be dispatched again from an exact current `main` commit to create a new uniquely identified test set.

## S04 formal package trust and workflow

S04 fixes the formal NuGet version at `0.10.0` and the runtime set at the same seven package IDs listed above. It does not publish symbol packages. The signing identity is deliberately self-signed and must never be described as publicly CA-trusted:

- trust model `PinnedSelfSigned`, `publicCaTrusted=false`, `internallyTrusted=true`, with exactly one `Current` signer;
- RSA-3072, subject `CN=CP6 Platform Release Signing`, SHA-256 with RSA, critical `DigitalSignature` key usage, code-signing EKU, subject-key identifier, and critical `CA=false` basic constraints; and
- one required SHA-256 RFC 3161 timestamp from `http://timestamp.digicert.com`, whose normal system-root chain is independently validated online.

The byte-identical public trust assets now exist at these exact paths in
Platform, CRM, and public CP6:

```text
eng/p10/trust/p10-formal-nuget-trust-store.v1.json
eng/p10/trust/certificates/1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d.cer
```

Only those public bytes may be copied byte-for-byte to the same relative paths in `CP6.CRM` and public `CP6`. Private PFX bytes and their password may exist only as the two `p10-formal-release` Environment Secrets `P10_NUGET_SIGNING_PFX_BASE64` and `P10_NUGET_SIGNING_PFX_PASSWORD`, plus a job-scoped Windows temporary file removed in `finally` and unconditional workflow cleanup.

The manual exact-main workflow is `.github/workflows/p10-formal-packages.yml`. Its protected `windows-2025` job preflights, builds once, signs, verifies, publishes without skip/overwrite semantics, downloads every package from GitHub Packages, and uploads only public read-back evidence. A dependent `ubuntu-latest` job verifies the same seven read-back bytes in `Current` mode and creates the canonical final publication record. Both artifacts use `overwrite: false` and 90-day retention.

Local tooling verification is available now:

```powershell
pwsh -NoProfile -File tests/p10/formal-package-scripts.Tests.ps1
pwsh -NoProfile -File eng/verify.ps1 -Gate Contract -Profile ci
```

Before the one-time dispatch, the secret-bearing preflight validated the
protected Environment, exact remote `main`, absence of `0.10.0` for all seven
package IDs, PFX-to-public-trust identity, and the live RFC 3161 service. The
authorized invocation was:

```powershell
$sourceSha = '7a1a3e45019bd3a474610f1c49045a03ec741e5d'
gh workflow run p10-formal-packages.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$sourceSha -f version=0.10.0
```

That command was dispatched exactly once for `0.10.0`; the consumed version
must never be republished, deleted, overwritten, or retried. Before dispatch,
`S04_EXTERNAL_PREREQUISITES_READY=true`: both formal signing Secrets existed in
the protected Environment, the PFX matched the merged public trust, and the
public certificate and policy bytes matched across Platform, CRM, and CP6.

## S04 external prerequisite evidence

Public CP6 `main@96ba90acf24cdf8be37c48cf94e5bc5d3f4fb3d7` records the
bucket-scoped R2 publisher, permanent read-only consumer, non-mutating
900-second temporary-credential preflight, and separate locator/OCI cosign
identities. The canonical storage/cosign trust SHA-256 is
`0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9`.
The R2 authority remains `cp6-release-r2-v1`, bucket `cp6-release`, default
jurisdiction, with only `candidates/platform/` and `objects/sha256/` accepted.

`GTX537/CP6.Platform` was reconfirmed public. Environment
`p10-formal-release` (ID `21135999336`) has required reviewer `GTX537`,
`prevent_self_review=false`, custom branch policies, and exactly one `main`
branch policy. It contained no Secrets during this gate; repository-level
fallback Secrets were not created.

The non-secret diagnostic workflow
`.github/workflows/p10-rfc3161-preflight.yml` was merged by PR #44 at
`main@df7388e6c3787f83dc74345513ce1adfe6c1ed5b`. Exact-main run
`33714794599`, attempt `1`, passed on `ubuntu-latest`, `windows-2025`, and the
aggregate job. Both live SHA-256 probes returned policy OID
`2.16.840.1.114412.7.1` and built normal online-revocation system trust paths.
Linux recorded the three-certificate chain:

```text
4aa03fa22cd75c84c55c938f828e676b9caecab33fe36d269aa334f146110a33
ca0b1554ecd901ea19dcad8749e9f2648c8d6dfcea1add9d2c2109415bb82ccd
552f7bdcf1a7af9e6ce672017f4f12abf77240c78e761ac203d1d9d20ac89988
```

Windows recorded the four-certificate chain:

```text
4aa03fa22cd75c84c55c938f828e676b9caecab33fe36d269aa334f146110a33
ca0b1554ecd901ea19dcad8749e9f2648c8d6dfcea1add9d2c2109415bb82ccd
33846b545a49c9be4903c60e01713c1bd4e4ef31ea65cd95d69e62794f30b941
3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c
```

The OS-specific root paths are expected and retained independently; both share
the same timestamp signer and issuing intermediate. Aggregate artifact ID
`9878130997` has digest
`sha256:e8a007e8e4b96f5aea6d5a277b144072a20a8c8eda4b0cb65a2006546786fff5`.
Independent download validation confirmed the exact main/run tuple, canonical
UTC timestamps, runner-specific identities, and a clean secret-shaped-content
scan. This evidence is a preflight only and authorizes no publication.

## S04 formal publication evidence

The formal Platform package publication was executed and independently closed
on 2026-09-03. **P10 S04 is complete. P10 overall remains Candidate / No-Go
pending S05 and S06.** No R2 object, candidate, Locator, image, or environment
deployment was created or authorized by this work.

| Identity | Exact value |
| --- | --- |
| Formal package source | Platform `main@7a1a3e45019bd3a474610f1c49045a03ec741e5d` |
| Formal workflow | `.github/workflows/p10-formal-packages.yml`; Git blob `b530a8b24f9c56d9c8e49d6cfce2fa5beb23216f` |
| Original publication run | `33759030780`, attempt `1`, workflow conclusion `failure` |
| Windows publication job | `sign-publish=success`; seven packages published and read back |
| Original Linux job | seven-package verification succeeded; final aggregation failed on an invalid full-chain-equality assumption |
| Windows read-back artifact | ID `9894785111`; `p10-s04-windows-readback-7a1a3e45019bd3a474610f1c49045a03ec741e5d-1` |
| Windows artifact digest | `sha256:a4b4154b1ef4060d07d438c5931ad2b08399eb9c3c1f18d95cbe73a89f84ba67` |
| Windows artifact expiry | `2026-12-02T13:05:22Z` |
| Cross-platform recovery merge | Platform `main@52349c5926cdaab7168dcb11a382de7a067f971d` |
| First read-only recovery run | `33764257472`, attempt `1`, `failure`; no artifact; UTC format literal defect after successful package verification |
| Timestamp-fix merge and exact-main gate | Platform `main@c6c129e3a37cfe4f02d961a1981f69b8942a7268`; validation run `33765895899=success` |
| Successful recovery workflow | Git blob `5db2d96df0d1042ed147bc1fdb79017366a96685`; run `33766967322`, attempt `1`, `success` |
| Final immutable artifact | ID `9897939979`; `p10-s04-final-publication-7a1a3e45019bd3a474610f1c49045a03ec741e5d-33759030780-recovery-1` |
| Final artifact digest and expiry | `sha256:f32d8365771d851de2e2da765e6a8d546a2267555ec4c4596c02a932b0720bba`; `2026-12-02T14:27:40Z` |
| Canonical final-publication JSON | `sha256:407357159e707f316a5cbf2c6be59e69dd4b922f6efd66df5bcdad78e2bc1963` |
| Linux verification JSON | `sha256:53bd6004f5f40c801f4758695848fba7836ec665b569a73fc8fbe21f4b85bd3a` |
| Recovery provenance JSON | `sha256:9f7e3cea3281029be38cd150117e03b554a4166996cd4084f958cf6c809ff49b` |

The original run is intentionally retained as failed: its protected Windows
job consumed `0.10.0` successfully, while the dependent job rejected the valid
OS-specific RFC 3161 chain tails. The recovery workflow is manual and read-only
with only `actions: read` and `contents: read`; it binds the original run,
attempt, source commit, artifact ID, artifact digest, and package version. It
cannot publish, delete, overwrite, write R2, or deploy. The timestamp-format
failure in the first recovery run was fixed through PR #49 with a regression
contract before the successful run; `0.10.0` was never republished.

The formal trust identity is:

| Trust field | Exact value |
| --- | --- |
| Trust model | `PinnedSelfSigned`; `internallyTrusted=true`; `publicCaTrusted=false` |
| Policy version and SHA-256 | `1`; `da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f` |
| Certificate SHA-256 | `1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d` |
| SPKI key ID | `sha256:27ecc2239a1b3c2368610d3602aadc5260b44e26baffe896b9a2449662c696d6` |
| Subject and issuer | `CN=CP6 Platform Release Signing` |
| Validity | `2026-09-03T04:56:13.000Z` through `2028-09-02T05:01:13.000Z` |
| RFC 3161 policy OID | `2.16.840.1.114412.7.1` |
| Timestamp leaf certificate SHA-256 | `4aa03fa22cd75c84c55c938f828e676b9caecab33fe36d269aa334f146110a33` |
| Trust synchronization | Platform `7a1a3e45019bd3a474610f1c49045a03ec741e5d`; CRM `4a340042f3596e82e7d358a39ad3106933c4395d`; CP6 `5881ce8987f63b0c05ae28c2470d6e56d21e5011` |

Windows recorded the four-certificate path and Linux the three-certificate
path listed in the external prerequisite section above. Both independently
built valid online system-root chains, share the timestamp leaf and issuing
intermediate, and differ only in the OS-selected valid tail. The recovery keeps
the package hash, author signer, SPKI, timestamp OID, timestamp leaf, and source
identity invariant while retaining each platform's complete chain separately.

| Package | GitHub Packages version | Published/read-back SHA-256 |
| --- | --- | --- |
| `CP6.Platform.Abstractions` | [`0.10.0` / `1205108513`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Abstractions/1205108513) | `08280769c339266d472fdce8322e75ab933c8002a1cfbbd40e4b565f9c5dc07f` |
| `CP6.Platform.AspNetCore` | [`0.10.0` / `1205108604`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.AspNetCore/1205108604) | `a77610f44a4cd61b7eaacceefc4d1fe1cbc99ab76b99576a1d5b627595f69e1e` |
| `CP6.Platform.Contracts` | [`0.10.0` / `1205108666`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Contracts/1205108666) | `cee3b5c105315cca6ce66469826c4cc667be869566fe475689434bc849d0869e` |
| `CP6.Platform.Deployment` | [`0.10.0` / `1205108745`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Deployment/1205108745) | `323acdf8c96b91ab6785090c36e5358e38d3c5800e5a36504ff4b03766de8cfc` |
| `CP6.Platform.EntityFramework` | [`0.10.0` / `1205108826`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.EntityFramework/1205108826) | `c0413a9f32d6638639809253670f8230dd1eeaf29e8b62c38bf6cc50f357599d` |
| `CP6.Platform.Messaging` | [`0.10.0` / `1205108903`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Messaging/1205108903) | `dc13c6c1ae1fd1c86a759bbf5dfd77d4ef16b7eb2b92f0c6010063e1c5094026` |
| `CP6.Platform.Release` | [`0.10.0` / `1205108972`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Release/1205108972) | `dcae792e494cb4f4369d6ed777cced8054ca0cb13bd0afe2bdc130667fa52caf` |

Independent artifact inspection confirmed exactly three final public JSON
records, seven unique package identities, byte-preserving feed transformation,
matching author-signed and published hashes, `windows=Success`,
`linux=Success`, and `publicCaTrusted=false`. `CP6.Platform.ReleaseTool
validate-formal-publication` accepted the canonical final record against the
pinned policy and certificate at evaluation time `2026-09-03T14:29:54.701Z`.

## Local validation

Run the contract and script tests with .NET 8 and PowerShell 7:

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release
pwsh -NoProfile -File tests/p10/test-package-scripts.Tests.ps1
pwsh -NoProfile -File eng/verify.ps1 -Gate Contract -Profile ci
```

The full package lifecycle requires Windows for NuGet package signing, but it requires no machine or user root-store mutation. It generates only test packages under `artifacts/p10-test/` and never pushes them.

## S02 dispatch and verification

S02 is a manual exact-main workflow. First resolve the approved `main` SHA, then dispatch with that same value:

```powershell
$sourceSha = gh api repos/GTX537/CP6.Platform/commits/main --jq .sha
gh workflow run p10-test-candidate.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$sourceSha
```

The workflow fails unless the dispatch ref is `main`, the event-frozen `github.sha` equals `expected_commit`, and checkout HEAD equals that same SHA. It performs one restore/build, runs Architecture, Unit, and Release gates, packs/signs/verifies the exact package set, scans for private material, uploads the package artifact, queries that exact artifact by ID, and creates the transport record from API metadata.

For an authorized S03 test consumer, download both artifacts from the selected successful run and verify locally:

```powershell
gh run download <run-id> --repo GTX537/CP6.Platform --name p10-s02-packages-<source-sha>-<attempt> --dir artifacts/p10-test/download/packages
gh run download <run-id> --repo GTX537/CP6.Platform --name p10-s02-transport-<source-sha>-<attempt> --dir artifacts/p10-test/download/transport
$cer = [IO.File]::ReadAllBytes('artifacts/p10-test/download/packages/test-signing-public.cer')
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cer)).ToLowerInvariant()
pwsh -NoProfile -File eng/p10/Test-P10TestPackageSet.ps1 -PackagePath artifacts/p10-test/download/packages -ExpectedSourceGitSha <source-sha> -ExpectedRunId <run-id> -ExpectedRunAttempt <attempt> -ExpectedCertificateFingerprint $fingerprint
pwsh -NoProfile -File eng/p10/Test-P10TransportRecord.ps1 -TransportPath artifacts/p10-test/download/transport/test-package-transport.v1.json -EvaluationUtc ([DateTimeOffset]::UtcNow.UtcDateTime.ToString('O'))
```

The consumer must compare the transport source/run tuple and artifact ID/digest with the GitHub API response before using the package directory as an ephemeral local source.

## S02 exact test-candidate evidence

The following test-only evidence was produced and independently verified on 2026-09-01. It does not advance the status beyond **Implemented / Test Candidate**.

| Identity | Exact value |
| --- | --- |
| Platform source SHA | `f09773a8cbd32c27ba531f5c02f52d32ef534fb3` |
| Workflow path | `.github/workflows/p10-test-candidate.yml` |
| Workflow file Git blob SHA | `1c0ade934712c245435d373fb4c4141f6f6ffb68` |
| Workflow run | `33546570407`, attempt `1`, conclusion `success` |
| Package artifact | ID `9815880292`; `p10-s02-packages-f09773a8cbd32c27ba531f5c02f52d32ef534fb3-1` |
| Package artifact digest | `sha256:c0d79908d874fa836e1a296e94bb8955d07add5a27de20351b489385ced44aeb` |
| Package artifact UTC window | created `2026-09-01T19:01:09Z`; expires `2026-11-30T18:56:17Z` |
| Transport artifact | ID `9815889756`; `p10-s02-transport-f09773a8cbd32c27ba531f5c02f52d32ef534fb3-1` |
| Transport artifact digest | `sha256:4e2ff548297e526c6a8797ec81d95cf8cb1db20ebeb4d185cf9cd8461a2af5a5` |
| Transport artifact UTC window | created `2026-09-01T19:01:24Z`; expires `2026-11-30T18:56:17Z` |
| Test package version | `0.10.0-test.f09773a8cbd3.1` |
| Test certificate SHA-256 fingerprint | `cefa6a52b6b9a50a9e2f992e3aecf7f118e765bae0da1ab2fa83eff4c02d6a10` |

Independent verification downloaded both artifacts by their exact names and run ID into a clean worktree at the recorded Platform source SHA. `Test-P10TestPackageSet.ps1` accepted exactly seven `.nupkg` and seven `.snupkg` files, all seven manifest entries, hashes, source/run identities, test-only markers, and signatures pinned to the fingerprint above. `Test-P10TransportRecord.ps1` accepted the canonical transport record and its binding to package artifact ID `9815880292`, package digest `sha256:c0d79908d874fa836e1a296e94bb8955d07add5a27de20351b489385ced44aeb`, run `33546570407` attempt `1`, and Platform SHA `f09773a8cbd32c27ba531f5c02f52d32ef534fb3`. Both validators started and ended without repository-default ReleaseTool output, and left no private build residue.

## Evidence still required after S04 completion

The following inputs remain unavailable or incomplete after the formal package
publication:

- exact GHCR repository digests, signature bundles, SBOMs, scans, and deployable image evidence;
- S05 CRM consumption of the exact formal package version with no `ProjectReference` fallback;
- exact CP6 publisher identity and completed cross-repository gate evidence;
- real CP6.Portal source, image, and schema evidence;
- immutable public evidence objects and signed Locator bytes;
- S06 final decision evidence; and
- any separately approved environment deployment request.

Until those inputs pass the public validators, P10 remains **Candidate / No-Go
pending S05 and S06**. S04 package publication is complete; System candidate
publication, Locator/R2 publication, and deployment remain explicitly not
authorized.
