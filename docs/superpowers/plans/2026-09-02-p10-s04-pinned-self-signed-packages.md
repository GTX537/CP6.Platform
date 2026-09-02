# P10 S04 Pinned Self-Signed Packages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the exact seven CP6.Platform packages once from an exact `main` commit, sign them with the reviewed pinned self-signed RSA-3072 identity and a real RFC3161 timestamp, publish immutable version `0.10.0` to GitHub Packages, read the package bytes back unchanged, and produce independently verified Windows and Linux evidence for S05.

**Architecture:** A new formal lane remains physically and semantically separate from S02. `CP6.Platform.Release` owns two new canonical supporting contracts, a pinned-NuGet trust-policy parser, certificate-profile validation, and cross-platform NuGet signature/timestamp verification. PowerShell orchestrates build/sign/publish/cleanup; `p10-formal-packages.yml` supplies the protected GitHub Environment and two-OS gates. The real private key exists only in the `p10-formal-release` Environment Secrets and a job-scoped Windows temporary PFX that is deleted unconditionally. The public certificate and canonical trust policy are committed out of band to Platform, CRM, and public CP6 before the first publication.

**Tech Stack:** .NET 8/C# 12, xUnit 2.9.3, NuGet.Packaging/Protocol/Versioning 6.11.2, `System.Security.Cryptography.Pkcs`, PowerShell 7/Pester, GitHub Actions on `windows-2025` and `ubuntu-latest`, GitHub Packages, DigiCert RFC3161.

---

## Scope and delivery boundary

This plan implements P10 S04 plus the public trust bootstrap needed by S05 and
S06. It does not implement the CRM S05 consumer workflow, the public CP6 S06
candidate/Locator workflows, cosign or R2 provisioning, or any deployment.

The implementation is deliberately split into three irreversible boundaries:

1. land code and workflow without dispatching it;
2. create the protected signing identity and merge identical public trust into
   all three repositories; and
3. publish `0.10.0` exactly once only after every external prerequisite passes.

Failure before the first package upload does not consume `0.10.0`. Any upload
attempt consumes `0.10.0`, even if a later package, read-back, or Linux gate
fails. No task deletes, overwrites, or unlists a package version.

## Fixed values

| Item | Exact value |
| --- | --- |
| Formal version | `0.10.0` |
| Package feed | `https://nuget.pkg.github.com/GTX537/index.json` |
| Signing environment | `p10-formal-release` |
| Signing runner | `windows-2025` |
| Linux verifier | `ubuntu-latest` |
| Certificate subject/issuer | `CN=CP6 Platform Release Signing` |
| Certificate key/profile | RSA-3072, SHA256withRSA, CA=false, DigitalSignature, code-signing EKU, SKI |
| Certificate validity | bootstrap UTC minus 5 minutes through bootstrap UTC plus 730 days |
| Timestamp service | `http://timestamp.digicert.com` |
| Timestamp hash | SHA-256 |
| Trust model | `PinnedSelfSigned` |
| Public-CA claim | `publicCaTrusted=false` |
| Internal claim | `internallyTrusted=true` |
| PFX Secrets | `P10_NUGET_SIGNING_PFX_BASE64`, `P10_NUGET_SIGNING_PFX_PASSWORD` |
| Trust policy | `eng/p10/trust/p10-formal-nuget-trust-store.v1.json` |
| CER directory | `eng/p10/trust/certificates/` |
| Build invocation | `p10-s04:{sourceGitSha}:{runId}:{runAttempt}` |
| Required runtime packages | 7 `.nupkg`; `CP6.Platform.Testing` and `.snupkg` do not count |

The exact package IDs, in ordinal order, are:

```text
CP6.Platform.Abstractions
CP6.Platform.AspNetCore
CP6.Platform.Contracts
CP6.Platform.Deployment
CP6.Platform.EntityFramework
CP6.Platform.Messaging
CP6.Platform.Release
```

## File structure

Platform implementation files:

```text
.github/workflows/p10-formal-packages.yml
contracts/release/v1/
  pinned-nuget-trust-store.v1.schema.json
  formal-package-publication.v1.schema.json
  assets.v1.json
  fixtures/supporting/pinned-nuget-trust.*.json
  fixtures/supporting/formal-package-publication.*.json
eng/p10/
  Initialize-P10FormalCertificate.ps1
  Pack-P10FormalPackages.ps1
  New-P10FormalPackageSet.ps1
  Publish-P10FormalPackageSet.ps1
  Test-P10FormalPackageSet.ps1
  New-P10FormalPublicationRecord.ps1
  Test-P10FormalPrerequisites.ps1
eng/p10/trust/
  certificates/{certificateSha256}.cer
  p10-formal-nuget-trust-store.v1.json
src/CP6.Platform.Release/
  Cp6PinnedNuGetTrustPolicy.cs
  Cp6NuGetCertificateProfile.cs
  Cp6FormalPackagePublicationValidator.cs
  Cp6ReleaseContractIds.cs
  Cp6ReleaseValidator.cs
tools/CP6.Platform.ReleaseTool/
  Program.cs
  FormalPackageVerifier.cs
  NuGetPackageDownloader.cs
tests/CP6.Platform.ReleaseTests/
  PinnedNuGetTrustPolicyTests.cs
  FormalPackagePublicationTests.cs
  FormalCertificateProfileTests.cs
  FormalPackageVerifierTests.cs
  P10FormalWorkflowContractTests.cs
tests/p10/formal-package-scripts.Tests.ps1
docs/P10-RELEASE-GOVERNANCE.md
```

Trust-bootstrap-only files in the downstream repositories:

```text
D:\CP6.CRM\eng\p10\trust\certificates\{certificateSha256}.cer
D:\CP6.CRM\eng\p10\trust\p10-formal-nuget-trust-store.v1.json
D:\CP6\eng\p10\trust\certificates\{certificateSha256}.cer
D:\CP6\eng\p10\trust\p10-formal-nuget-trust-store.v1.json
```

The fingerprint-derived CER filename is the only runtime-derived path in this
plan. Every command computes it from the DER bytes and checks it rather than
accepting a human-entered filename.

## Task 0: Land the approved design and create a clean implementation branch

**Files:**

- Existing: `docs/superpowers/specs/2026-09-01-p10-release-governance-design.md`
- Existing: `docs/superpowers/specs/2026-09-02-p10-pinned-self-signed-trust-design.md`
- New: `docs/superpowers/plans/2026-09-02-p10-s04-pinned-self-signed-packages.md`

- [ ] **Step 1: Verify the design branch is documentation-only**

```powershell
git status --short --branch
git diff --check
git diff origin/main...HEAD --stat
git diff origin/main...HEAD -- src tests contracts eng .github
```

Expected: the last command prints nothing; only the two specs and this plan
differ from `origin/main`.

- [ ] **Step 2: Push, review, and merge the design PR**

```powershell
git push -u origin codex/p10-self-signed-formal-trust-design
$designPr = gh pr create --repo GTX537/CP6.Platform --base main --head codex/p10-self-signed-formal-trust-design --title 'docs(release): approve pinned self-signed P10 trust' --body 'Approves the zero-certificate-cost P10 formal NuGet trust amendment and its S04 implementation plan. It does not publish packages, create Secrets, write R2, or deploy.'
$designPrNumber = gh pr view $designPr --repo GTX537/CP6.Platform --json number --jq .number
gh pr checks $designPrNumber --repo GTX537/CP6.Platform --watch
gh pr merge $designPrNumber --repo GTX537/CP6.Platform --merge
```

Expected: all checks pass and the PR state is `MERGED`. Do not bypass a failed
check.

- [ ] **Step 3: Create a new implementation worktree from exact remote main**

Run from `D:\CP6\CP6.Platform`:

```powershell
git fetch origin main --prune
$implementationRoot = 'D:\CP6.Platform-worktrees\p10-s04-formal-packages'
git worktree add -b codex/p10-s04-formal-packages $implementationRoot origin/main
git -C $implementationRoot status --short --branch
git -C $implementationRoot rev-parse HEAD
```

Expected: a clean worktree on `codex/p10-s04-formal-packages`, based on the
merged design commit. Do not reuse or clean another worktree.

- [ ] **Step 4: Establish a real .NET 8 baseline**

```powershell
dotnet --list-sdks
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Build -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
```

Expected: .NET 8 is listed and all gates pass. The planning computer currently
has only .NET 10, so use the repository CI matrix if .NET 8 is still absent;
never edit `global.json` to conceal the mismatch.

## Task 1: Freeze canonical formal trust and publication contracts

**Files:**

- Create: `contracts/release/v1/pinned-nuget-trust-store.v1.schema.json`
- Create: `contracts/release/v1/formal-package-publication.v1.schema.json`
- Modify: `contracts/release/v1/build-invocation-provenance.v1.schema.json`
- Modify: `contracts/release/v1/assets.v1.json`
- Modify: `src/CP6.Platform.Release/Cp6ReleaseContractIds.cs`
- Create: `contracts/release/v1/fixtures/supporting/pinned-nuget-trust.valid.json`
- Create negative trust fixtures for public-CA claim, wrong package set, two current signers, bad fingerprint, bad status fields, and noncanonical bytes
- Create: `contracts/release/v1/fixtures/supporting/formal-package-publication.valid.json`
- Create negative publication fixtures for mixed versions, changed published bytes, test timestamp policy, and a missing package
- Modify: `tests/CP6.Platform.ReleaseTests/ReleaseSchemaAssetTests.cs`
- Modify: `tests/CP6.Platform.ReleaseTests/SupportingContractValidationTests.cs`

- [ ] **Step 1: Add failing asset and fixture tests**

Add these constants and assertions first:

```csharp
public const string FormalPackagePublication =
    "https://schemas.cp6.dev/release/formal-package-publication.v1";
public const string PinnedNuGetTrustStore =
    "https://schemas.cp6.dev/release/pinned-nuget-trust-store.v1";
```

```csharp
[Fact]
public void Formal_contract_assets_are_closed_and_registered_once()
{
    var path = Path.Combine(
        ReleaseTestData.RepositoryRoot,
        "contracts", "release", "v1", "assets.v1.json");
    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var ids = document.RootElement.GetProperty("schemas")
        .EnumerateArray()
        .Select(item => item.GetProperty("id").GetString())
        .ToArray();
    Assert.Single(ids, id => id == Cp6ReleaseContractIds.PinnedNuGetTrustStore);
    Assert.Single(ids, id => id == Cp6ReleaseContractIds.FormalPackagePublication);
}
```

Run:

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~Formal_contract_assets_are_closed_and_registered_once'
```

Expected: FAIL because the IDs and schemas are absent.

- [ ] **Step 2: Add the exact pinned-NuGet schema**

Require this closed root shape:

```json
{
  "$schemaId": "https://schemas.cp6.dev/release/pinned-nuget-trust-store.v1",
  "policyVersion": 1,
  "trustModel": "PinnedSelfSigned",
  "publicCaTrusted": false,
  "internallyTrusted": true,
  "timestampPolicy": "Rfc3161Required",
  "timestampService": "http://timestamp.digicert.com",
  "allowedPackageIds": [],
  "signers": []
}
```

Each signer requires exactly:

```json
{
  "certificatePath": "certificates/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.cer",
  "certificateSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "spkiKeyId": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
  "subject": "CN=CP6 Platform Release Signing",
  "issuer": "CN=CP6 Platform Release Signing",
  "validFromUtc": "2026-09-02T00:00:00.000Z",
  "validUntilUtc": "2028-09-01T00:05:00.000Z",
  "status": "Current",
  "activatedAtUtc": "2026-09-02T00:05:00.000Z",
  "revokedAtUtc": null,
  "revocationReason": null
}
```

Use `oneOf` so `Revoked` requires non-null `revokedAtUtc` and
`revocationReason`, while `Current` and `Historical` require both to be JSON
`null`. Require one to many signers, unique items, and exactly seven package
IDs.

- [ ] **Step 3: Add the formal-publication schema and S04 invocation pattern**

Require a closed root with `createdAtUtc`, `version`, `sourceGitSha`,
`buildInvocationId`, `workflow`, `toolchain`, `trust`, `packages`, and
`verification`. The package element is the same nine-field object consumed by
`Cp6ReleaseValidator`, plus `timestampPolicyOid` and
`timestampCertificateChainSha256`:

```json
{
  "packageId": "CP6.Platform.Abstractions",
  "version": "0.10.0",
  "sourceGitSha": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
  "authorSignedPackageSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "publishedPackageSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "feedIdentity": "https://nuget.pkg.github.com/GTX537/index.json#CP6.Platform.Abstractions/0.10.0",
  "feedTransformation": "BytePreserving",
  "signerFingerprint": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
  "timestampPolicy": "Rfc3161Required",
  "timestampPolicyOid": "1.2.3.4",
  "timestampCertificateChainSha256": ["dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"]
}
```

`verification` is exactly `{"windows":"Success","linux":"Success"}`.
Extend the build-provenance pattern from only `p10-s02` to:

```regex
^p10-s0(?:2|4):[0-9a-f]{40}:[1-9][0-9]*:[1-9][0-9]*$
```

- [ ] **Step 4: Register both schemas and create canonical fixtures**

Insert both IDs and paths into `assets.v1.json` and `Cp6ReleaseContractIds.All`
in ordinal ID order. Generate fixture bytes through the existing
`CP6.Platform.ReleaseTool canonicalize` command; do not hand-format canonical
JSON.

- [ ] **Step 5: Run schema tests and commit**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~ReleaseSchemaAssetTests|FullyQualifiedName~SupportingContractValidationTests'
git diff --check
git add contracts/release/v1 src/CP6.Platform.Release/Cp6ReleaseContractIds.cs tests/CP6.Platform.ReleaseTests/ReleaseSchemaAssetTests.cs tests/CP6.Platform.ReleaseTests/SupportingContractValidationTests.cs
git commit -m 'feat(release): define formal NuGet evidence contracts'
```

Expected: PASS; the commit contains schemas, fixtures, IDs, and tests only.

## Task 2: Implement the pinned self-signed trust policy

**Files:**

- Create: `src/CP6.Platform.Release/Cp6PinnedNuGetTrustPolicy.cs`
- Create: `src/CP6.Platform.Release/Cp6NuGetCertificateProfile.cs`
- Create: `tests/CP6.Platform.ReleaseTests/PinnedNuGetTrustPolicyTests.cs`
- Create: `tests/CP6.Platform.ReleaseTests/FormalCertificateProfileTests.cs`

- [ ] **Step 1: Write failing policy tests**

The core positive test must load the canonical fixture and a DER certificate
map, then assert exact claims:

```csharp
var policy = Cp6PinnedNuGetTrustPolicy.Parse(json, certificates);
Assert.Equal("PinnedSelfSigned", policy.TrustModel);
Assert.False(policy.PublicCaTrusted);
Assert.True(policy.InternallyTrusted);
Assert.Equal("Rfc3161Required", policy.TimestampPolicy);
Assert.Equal(new Uri("http://timestamp.digicert.com"), policy.TimestampService);
Assert.Equal(7, policy.AllowedPackageIds.Count);
Assert.Equal("Current", policy.CurrentSigner.Status);
```

Add separate tests that reject `publicCaTrusted=true`, two current signers,
filename/hash/DER mismatches, an S02 subject, RSA-2048, missing EKU, CA=true,
missing SKI, expired/not-yet-valid current use, and revoked current use.

Run:

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~PinnedNuGetTrustPolicyTests|FullyQualifiedName~FormalCertificateProfileTests'
```

Expected: FAIL because both public types are absent.

- [ ] **Step 2: Parse canonical policy bytes and content-addressed CERs**

Use a side-effect-free resolver boundary:

```csharp
public static Cp6PinnedNuGetTrustPolicy Parse(
    ReadOnlySpan<byte> utf8Json,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> certificatesByPath)
```

Reject noncanonical JSON, unknown properties, any trust claim different from
the fixed values, non-ordinal package IDs, anything other than exactly one
`Current` signer, and any certificate path not matching
`^certificates/[0-9a-f]{64}\.cer$`.

- [ ] **Step 3: Validate the exact X.509 profile**

`Cp6NuGetCertificateProfile.Validate` must enforce:

```csharp
certificate.SubjectName.Name == "CN=CP6 Platform Release Signing"
certificate.IssuerName.Name == certificate.SubjectName.Name
certificate.GetRSAPublicKey()!.KeySize == 3072
certificate.SignatureAlgorithm.Value == "1.2.840.113549.1.1.11"
basicConstraints.CertificateAuthority == false && basicConstraints.Critical
keyUsage.KeyUsages == X509KeyUsageFlags.DigitalSignature && keyUsage.Critical
eku.EnhancedKeyUsages.Cast<Oid>().Single().Value == "1.3.6.1.5.5.7.3.3"
subjectKeyIdentifier is not null
```

Compute identities from bytes, never from claims:

```csharp
var certificateSha256 = Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();
var spki = certificate.GetRSAPublicKey()!.ExportSubjectPublicKeyInfo();
var spkiKeyId = "sha256:" + Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
```

- [ ] **Step 4: Implement signer state rules**

`RequireSigner(fingerprint, signedAtUtc, evaluationUtc,
Cp6ReleaseValidationMode.Current)` accepts only `Current`, requires signing time
inside certificate validity and after activation, and rejects a signer whose
revocation is effective. Historical audit may inspect `Historical` and
`Revoked`, but must report current revocation separately and must never make a
revoked signer consumable.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~PinnedNuGetTrustPolicyTests|FullyQualifiedName~FormalCertificateProfileTests'
git diff --check
git add src/CP6.Platform.Release/Cp6PinnedNuGetTrustPolicy.cs src/CP6.Platform.Release/Cp6NuGetCertificateProfile.cs tests/CP6.Platform.ReleaseTests/PinnedNuGetTrustPolicyTests.cs tests/CP6.Platform.ReleaseTests/FormalCertificateProfileTests.cs
git commit -m 'feat(release): validate pinned NuGet signing trust'
```

Expected: PASS with all negative cases fail-closed.

## Task 3: Add independent formal package and RFC3161 verification

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj`
- Modify: `tools/CP6.Platform.ReleaseTool/Program.cs`
- Create: `tools/CP6.Platform.ReleaseTool/FormalPackageVerifier.cs`
- Create: `tools/CP6.Platform.ReleaseTool/NuGetPackageDownloader.cs`
- Create: `tests/CP6.Platform.ReleaseTests/FormalPackageVerifierTests.cs`

- [ ] **Step 1: Write failing CLI and verifier tests**

Add tests for an author-signed package with one valid RFC3161 token, then mutate
one condition at a time: unsigned, S02 signer, wrong pinned fingerprint, no
timestamp, two timestamps, bad timestamp imprint, untrusted TSA chain, wrong
package ID/version/source SHA, and tampered archive.

The command contract is:

```text
verify-formal-package artifacts/p10-formal/download/CP6.Platform.Abstractions.0.10.0.nupkg eng/p10/trust/p10-formal-nuget-trust-store.v1.json eng/p10/trust/certificates CP6.Platform.Abstractions 0.10.0 bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 2026-09-02T00:00:00.000Z Current
download-package https://nuget.pkg.github.com/GTX537/index.json CP6.Platform.Abstractions 0.10.0 artifacts/p10-formal/download/CP6.Platform.Abstractions.0.10.0.nupkg
validate-nuget-trust eng/p10/trust/p10-formal-nuget-trust-store.v1.json eng/p10/trust/certificates
```

Run the focused tests and expect failure because the commands do not exist.

- [ ] **Step 2: Add direct package dependencies**

Pin these centrally at `6.11.2` and reference them directly from the tool:

```xml
<PackageVersion Include="NuGet.Configuration" Version="6.11.2" />
<PackageVersion Include="NuGet.Protocol" Version="6.11.2" />
<PackageVersion Include="NuGet.Versioning" Version="6.11.2" />
<PackageVersion Include="System.Security.Cryptography.Pkcs" Version="8.0.1" />
```

Direct references avoid relying on transitive compile assets.

- [ ] **Step 3: Require an exact author signature and one timestamp**

Build the NuGet verifier with the pinned author fingerprint as the only
untrusted-root exception and an explicit strict settings object:

```csharp
var settings = new SignedPackageVerifierSettings(
    allowUnsigned: false,
    allowIllegal: false,
    allowUntrusted: false,
    allowIgnoreTimestamp: false,
    allowMultipleTimestamps: false,
    allowNoTimestamp: false,
    allowUnknownRevocation: false,
    reportUnknownRevocation: true,
    verificationTarget: VerificationTarget.Author,
    signaturePlacement: SignaturePlacement.PrimarySignature,
    repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
    revocationMode: RevocationMode.Online);
```

Providers remain `IntegrityVerificationProvider`,
`SignatureTrustAndValidityVerificationProvider` with only the exact pinned DER
fingerprint in `allowUntrustedRootList`, and an author-primary
`AllowListVerificationProvider`. Require `AuthorPrimarySignature`,
`result.IsSigned`, and `result.IsValid`.

- [ ] **Step 4: Parse and record the RFC3161 identity**

Read `.signature.p7s` with `SignedCms`. Require exactly one unsigned attribute
OID `1.2.840.113549.1.9.16.2.14`. Decode its timestamp CMS and ASN.1 `TSTInfo`;
record the numeric policy OID, generalized signing time, and leaf-to-root DER
SHA-256 chain. Require the time-stamping EKU
`1.3.6.1.5.5.7.3.8`, SHA-256 message imprint, and an online system-root chain
at the timestamp time. The pinned-author untrusted-root exception must never be
passed to this chain.

- [ ] **Step 5: Verify package metadata and stable version**

Use `NuGetVersion.TryParse`; require no prerelease label, no build metadata, and
`version == parsed.ToNormalizedString()`. Read the nuspec ID/version and the
repository commit metadata from the package. Require the exact requested ID,
`0.10.0`, and source SHA.

- [ ] **Step 6: Implement byte-preserving feed download**

`NuGetPackageDownloader` uses `SourceRepository` and
`FindPackageByIdResource.CopyNupkgToStreamAsync` to write the response directly
to a new destination file. It accepts credentials through the process
environment, never CLI arguments, and fails if the destination exists.

- [ ] **Step 7: Run focused tests on Windows and Linux CI, then commit**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~FormalPackageVerifierTests'
git diff --check
git add Directory.Packages.props tools/CP6.Platform.ReleaseTool tests/CP6.Platform.ReleaseTests/FormalPackageVerifierTests.cs
git commit -m 'feat(release): verify formal NuGet signatures and timestamps'
```

Expected: local PASS on a .NET 8 host and matrix PASS on Windows and Linux.

## Task 4: Implement the formal publication-record validator

**Files:**

- Create: `src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs`
- Modify: `src/CP6.Platform.Release/Cp6SupportingContractValidator.cs`
- Modify: `src/CP6.Platform.Release/Cp6ReleaseValidator.cs`
- Modify: `tools/CP6.Platform.ReleaseTool/Program.cs`
- Create: `tests/CP6.Platform.ReleaseTests/FormalPackagePublicationTests.cs`

- [ ] **Step 1: Add failing semantic tests**

Assert the valid fixture returns seven package IDs and seven subject hashes.
Assert exact error codes for mixed versions, source mismatch, non-stable
version, non-ordinal IDs, non-byte-preserving transformation, unequal signed
and published hashes, wrong feed, wrong signer, wrong timestamp policy, missing
chain, non-success verification, and a build invocation not bound to the source
SHA.

- [ ] **Step 2: Implement semantic validation**

Expose:

```csharp
public static Cp6ValidatedReleaseDocument ValidateFormalPackagePublication(
    ReadOnlySpan<byte> utf8Json,
    Cp6PinnedNuGetTrustPolicy trustPolicy,
    DateTimeOffset evaluationUtc)
```

Require `version == "0.10.0"` for this first formal publication, exact source
and build identity, the fixed workflow path/environment/repository, exact seven
IDs, one fingerprint selected by the pinned policy, `BytePreserving`, equal
hashes, fixed feed prefix, real timestamp metadata, and both gates `Success`.

Also extend package validation so formal candidate package records accept
`BytePreserving` only when the two hashes are identical and
`timestampPolicy=Rfc3161Required`. Existing `None`/`Documented` behavior remains
unchanged for older fixtures.

- [ ] **Step 3: Add the CLI command and run tests**

```text
validate-formal-publication artifacts/p10-formal/evidence/formal-package-publication.v1.json eng/p10/trust/p10-formal-nuget-trust-store.v1.json eng/p10/trust/certificates 2026-09-02T00:00:00.000Z
```

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~FormalPackagePublicationTests|FullyQualifiedName~PrimaryCandidateValidationTests'
git diff --check
git add src/CP6.Platform.Release tools/CP6.Platform.ReleaseTool/Program.cs tests/CP6.Platform.ReleaseTests/FormalPackagePublicationTests.cs
git commit -m 'feat(release): validate formal package publication evidence'
```

Expected: PASS, including all existing candidate fixtures.

## Task 5: Build a no-PFX-on-disk certificate bootstrap

**Files:**

- Create: `eng/p10/Initialize-P10FormalCertificate.ps1`
- Modify: `tests/p10/formal-package-scripts.Tests.ps1`

- [ ] **Step 1: Add static and injected-failure tests first**

The Pester test must assert the script contains no `Export-PfxCertificate`, no
PFX `WriteAllBytes`, no secret passed through `--body`, and no secret output.
It must inject a fake `gh` executable that consumes standard input, records
only input lengths, and fails on the second Secret write. After both success
and failure, assert no `.pfx`, `.p12`, `.pem`, `.key`, password file, or private
key remains under the test root.

- [ ] **Step 2: Generate the exact certificate in process memory**

Use `RSA.Create(3072)`, `CertificateRequest`, critical basic constraints and
key usage, code-signing EKU, and SKI. Generate a 16-byte serial and set its high
bit so it is 128-bit. Create the self-signed certificate with SHA256/RSA PKCS#1:

```powershell
$serial = [byte[]]::new(16)
[Security.Cryptography.RandomNumberGenerator]::Fill($serial)
$serial[0] = $serial[0] -bor 0x80
$issued = $request.Create(
    $subjectName,
    [Security.Cryptography.X509Certificates.X509SignatureGenerator]::CreateForRSA($rsa, [Security.Cryptography.RSASignaturePadding]::Pkcs1),
    $now.AddMinutes(-5),
    $now.AddDays(730),
    $serial)
$certificate = $issued.CopyWithPrivateKey($rsa)
```

Export only DER to `eng/p10/trust/certificates/{certificateSha256}.cer`. Keep PFX
bytes and the 32-random-byte base64 password in memory.

- [ ] **Step 3: Stream both Secrets to GitHub without arguments**

Use `ProcessStartInfo` with redirected standard input for:

```text
gh secret set P10_NUGET_SIGNING_PFX_BASE64 --repo GTX537/CP6.Platform --env p10-formal-release
gh secret set P10_NUGET_SIGNING_PFX_PASSWORD --repo GTX537/CP6.Platform --env p10-formal-release
```

Write the exact value to `StandardInput`, close it, require exit code zero, and
then require both names from `gh secret list --env p10-formal-release`. Never
log secret contents. Zero byte arrays and dispose RSA/certificate objects in
`finally`.

- [ ] **Step 4: Write and validate the public canonical trust store**

Create policy version 1 with one `Current` signer and canonicalize through the
ReleaseTool. Before success, run `validate-nuget-trust` against the generated
JSON and CER directory. On failure, retain no public half-state and instruct
rotation because a successfully written but subsequently lost Secret cannot be
reconstructed.

- [ ] **Step 5: Run Pester and commit the bootstrap code only**

```powershell
pwsh -NoProfile -File tests/p10/formal-package-scripts.Tests.ps1
git diff --check
git add eng/p10/Initialize-P10FormalCertificate.ps1 tests/p10/formal-package-scripts.Tests.ps1
git commit -m 'feat(release): add memory-only signing bootstrap'
```

Expected: PASS without creating a real GitHub Environment or Secret during the
test.

## Task 6: Implement one-build formal packing, signing, and local verification

**Files:**

- Create: `eng/p10/Pack-P10FormalPackages.ps1`
- Create: `eng/p10/New-P10FormalPackageSet.ps1`
- Create: `eng/p10/Test-P10FormalPackageSet.ps1`
- Modify: `tests/p10/formal-package-scripts.Tests.ps1`
- Modify: `eng/verify.ps1`

- [ ] **Step 1: Add failing script-contract tests**

Assert exact seven project paths, stable-version validation, one restore/build,
pack with `--no-build --no-restore`, no `CP6.Platform.Testing`, exact timestamp
URL and SHA-256 flags, no `--skip-duplicate`, no source push in the packing
script, and cleanup in `finally`.

- [ ] **Step 2: Pack exactly seven runtime packages from one build**

The orchestration is:

```powershell
dotnet restore CP6.Platform.sln
dotnet build CP6.Platform.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true -p:RepositoryCommit=$SourceGitSha
$projects = @(
  'src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj',
  'src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj',
  'src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj',
  'src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj',
  'src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj',
  'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj',
  'src/CP6.Platform.Release/CP6.Platform.Release.csproj'
)
foreach ($project in $projects) {
  dotnet pack $project --configuration Release --no-build --no-restore --output $unsignedRoot -p:PackageVersion=0.10.0 -p:ContinuousIntegrationBuild=true -p:RepositoryCommit=$SourceGitSha
}
```

Move `.snupkg` files to a separate public-symbol-evidence directory. Require
exactly seven runtime `.nupkg` names and reject any Testing package.

- [ ] **Step 3: Decode, match, and sign with one job-scoped PFX**

`New-P10FormalPackageSet.ps1` receives both Secrets only through environment
variables. It decodes into a cryptographically random directory under
`$env:RUNNER_TEMP`, loads the PFX with `EphemeralKeySet`, proves it matches the
committed Current signer, and signs each runtime package:

```powershell
dotnet nuget sign $package.FullName `
  --certificate-path $pfxPath `
  --certificate-password $env:P10_NUGET_SIGNING_PFX_PASSWORD `
  --hash-algorithm SHA256 `
  --timestamper 'http://timestamp.digicert.com' `
  --timestamp-hash-algorithm SHA256 `
  --overwrite
```

Password exposure is limited to the child process argument on the ephemeral
runner because `dotnet nuget sign` has no standard-input password option. Add
the exact password to the GitHub masking command before invocation and never
enable PowerShell tracing. This is the only approved exception to the
bootstrap's stricter no-secret-argument rule.

- [ ] **Step 4: Verify every signed package and provenance**

For each package, invoke `verify-formal-package` with exact ID/version/source
and Current mode. Write canonical build provenance with the
`p10-s04:{sourceGitSha}:{runId}:{runAttempt}` identity, pre-sign hashes, and final signed
hashes. Require no private material outside the random temp directory.

- [ ] **Step 5: Delete secret state unconditionally**

In `finally`, zero decoded PFX bytes, remove the PFX and its random directory,
clear local variables, and delete temporary NuGet credential files. A separate
workflow `if: always()` step repeats a residue scan and removal.

- [ ] **Step 6: Wire the formal Pester suite into the Contract gate**

Immediately after the S02 script test add:

```powershell
[void](Invoke-PowerShellStep -Name 'P10FormalPackageScriptContracts' -ScriptPath 'tests/p10/formal-package-scripts.Tests.ps1')
```

- [ ] **Step 7: Run tests and commit**

```powershell
pwsh -NoProfile -File tests/p10/formal-package-scripts.Tests.ps1
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
git diff --check
git add eng/p10/Pack-P10FormalPackages.ps1 eng/p10/New-P10FormalPackageSet.ps1 eng/p10/Test-P10FormalPackageSet.ps1 tests/p10/formal-package-scripts.Tests.ps1 eng/verify.ps1
git commit -m 'feat(release): build and verify formal package sets'
```

Expected: PASS. Tests may use an ephemeral test fixture certificate but must
label it synthetic and cannot produce formal acceptance evidence.

## Task 7: Implement immutable publish, read-back, and final evidence

**Files:**

- Create: `eng/p10/Test-P10FormalPrerequisites.ps1`
- Create: `eng/p10/Publish-P10FormalPackageSet.ps1`
- Create: `eng/p10/New-P10FormalPublicationRecord.ps1`
- Modify: `tests/p10/formal-package-scripts.Tests.ps1`

- [ ] **Step 1: Add failing version and partial-publication tests**

Use fake `gh`, `dotnet`, and downloader processes. Cover: all seven absent,
one existing version, first upload failure, fourth upload failure, read-back
hash mismatch, wrong downloaded identity, and cleanup failure. Assert the
script never uses `--skip-duplicate`, delete, overwrite, or unlist.

- [ ] **Step 2: Implement preflight without changing the feed**

Require:

- exact `main` event SHA and checkout SHA;
- canonical stable version exactly `0.10.0`;
- validated trust store and matching Environment PFX;
- all seven ID/version pairs absent through the GitHub package API;
- successful HTTPS/RFC3161 request/response preflight;
- required `p10-formal-release` Environment protection and the two Secret
  names;
- committed identical trust hashes supplied for Platform, CRM, and CP6; and
- explicit `S04_EXTERNAL_PREREQUISITES_READY=true` set only after cosign, R2,
  and permanent consumer credentials are separately recorded.

Any missing item exits before packing or upload.

- [ ] **Step 3: Publish in fixed ordinal order and burn on first attempt**

Immediately before the first upload, write a public local marker containing
version/source/run identity. Then push each exact file without
`--skip-duplicate`:

```powershell
dotnet nuget push $package.FullName --source 'https://nuget.pkg.github.com/GTX537/index.json' --api-key $env:GITHUB_TOKEN
```

On any failure, emit `p10-formal-version-consumed` and stop. Never delete or
repair the version.

- [ ] **Step 4: Download each package to a fresh directory**

Use the ReleaseTool `download-package` command, then compare raw SHA-256 with
the pre-upload signed hash and rerun `verify-formal-package`. Require seven
downloaded files and no extras.

- [ ] **Step 5: Create the final record only after both OS gates**

The Windows job emits public package data with its successful result. The Linux
job independently verifies downloaded bytes and invokes
`New-P10FormalPublicationRecord.ps1 -LinuxVerification Success`; that script
canonicalizes and validates `formal-package-publication.v1.json`. Evidence may
contain only public identities and hashes.

- [ ] **Step 6: Run Pester and commit**

```powershell
pwsh -NoProfile -File tests/p10/formal-package-scripts.Tests.ps1
git diff --check
git add eng/p10/Test-P10FormalPrerequisites.ps1 eng/p10/Publish-P10FormalPackageSet.ps1 eng/p10/New-P10FormalPublicationRecord.ps1 tests/p10/formal-package-scripts.Tests.ps1
git commit -m 'feat(release): publish immutable formal package evidence'
```

Expected: PASS; no test contacts or modifies GitHub Packages.

## Task 8: Add the protected two-OS formal workflow

**Files:**

- Create: `.github/workflows/p10-formal-packages.yml`
- Create: `tests/CP6.Platform.ReleaseTests/P10FormalWorkflowContractTests.cs`
- Modify: `tests/CP6.Platform.ReleaseTests/P10WorkflowContractTests.cs`

- [ ] **Step 1: Write failing workflow-contract tests**

Assert manual exact-main inputs `expected_commit` and `version`, exact version
`0.10.0`, `windows-2025`, environment `p10-formal-release`, job permissions
`contents: read` and `packages: write`, step-level Secret exposure, a dependent
`ubuntu-latest` job, pinned actions, publish before feed download, Windows
verification before upload, Linux verification after read-back, immutable
artifacts, and unconditional cleanup. Assert no deploy, R2, cosign, Azure,
`--skip-duplicate`, or package deletion text.

Run and expect failure because the workflow is absent.

- [ ] **Step 2: Implement `sign-publish`**

The job header is exactly:

```yaml
sign-publish:
  runs-on: windows-2025
  timeout-minutes: 45
  environment: p10-formal-release
  permissions:
    contents: read
    packages: write
```

Use the already pinned checkout, setup-dotnet, and upload-artifact commits.
Pass Secrets only to the signing step. Upload only downloaded public `.nupkg`,
public CER/trust, provenance, hashes, and sanitized Windows results with
`overwrite: false`.

- [ ] **Step 3: Implement independent `verify-linux`**

The Linux job depends on `sign-publish`, downloads the public artifact with
`actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093`, runs
the same formal verifier in Current mode on all seven feed-read-back package
bytes, creates the final canonical publication record, validates it, scans for
secret-shaped files/text, and uploads only the final evidence with
`overwrite: false`.

- [ ] **Step 4: Add unconditional cleanup and no-residue proof**

The Windows job ends with `if: ${{ always() }}` cleanup. It scans
`$env:RUNNER_TEMP` and `artifacts/p10-formal` for the task-specific PFX filename
or private extensions, removes job directories, and fails the job if residue
existed. Linux performs the same scan for transferred content.

- [ ] **Step 5: Run workflow and contract tests, then commit**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter 'FullyQualifiedName~P10FormalWorkflowContractTests|FullyQualifiedName~P10WorkflowContractTests'
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
git diff --check
git add .github/workflows/p10-formal-packages.yml tests/CP6.Platform.ReleaseTests/P10FormalWorkflowContractTests.cs tests/CP6.Platform.ReleaseTests/P10WorkflowContractTests.cs
git commit -m 'feat(release): add protected formal package workflow'
```

Expected: PASS. Do not dispatch the workflow.

## Task 9: Verify and land code without publishing

**Files:**

- Modify: `docs/P10-RELEASE-GOVERNANCE.md`

- [ ] **Step 1: Document the new readiness state truthfully**

Set Platform status to `S04 tooling implemented / publication not started`.
Document the fixed self-signed claims, exact trust paths, workflow, commands,
and outstanding external prerequisites. Keep formal package publication,
S05/S06, Locator, R2, and deployment explicitly incomplete.

- [ ] **Step 2: Run all proportional gates**

```powershell
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Build -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Security -Profile ci
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release
git diff --check
```

Expected: all pass on .NET 8. A timestamp-network integration test may be a
separate real preflight, but it may not be skipped for publication.

- [ ] **Step 3: Audit the complete diff and secret boundary**

```powershell
git diff origin/main...HEAD --stat
git diff origin/main...HEAD
rg -n --hidden -g '!artifacts/**' -g '!**/bin/**' -g '!**/obj/**' 'BEGIN (RSA |EC )?PRIVATE KEY|P10_NUGET_SIGNING_PFX_(BASE64|PASSWORD)\s*[:=]\s*[^$]' .
git status --short
```

Expected: no secrets, machine paths, test-only formal claims, unrelated files,
or uncommitted changes.

- [ ] **Step 4: Commit docs, push, review, and merge**

```powershell
git add docs/P10-RELEASE-GOVERNANCE.md
git commit -m 'docs(release): record S04 tooling readiness'
git push -u origin codex/p10-s04-formal-packages
$implementationPr = gh pr create --repo GTX537/CP6.Platform --base main --head codex/p10-s04-formal-packages --title 'feat(release): add P10 formal NuGet publication' --body 'Implements pinned self-signed trust, RFC3161 verification, immutable GitHub Packages publication tooling, and Windows/Linux gates. This PR does not create signing Secrets or dispatch publication.'
$implementationPrNumber = gh pr view $implementationPr --repo GTX537/CP6.Platform --json number --jq .number
gh pr checks $implementationPrNumber --repo GTX537/CP6.Platform --watch
gh pr merge $implementationPrNumber --repo GTX537/CP6.Platform --merge
```

Expected: required checks pass and the implementation PR is merged. No workflow
dispatch exists yet.

## Task 10: Prove Environment capability before creating a key

**External state:** GitHub repository `GTX537/CP6.Platform`.

This is a hard gate. GitHub's current product rules require Pro/Team/Enterprise
for Environment Secrets in a private repository, and required reviewers in a
private repository may require Enterprise. The confirmed design is not silently
downgraded if the account lacks either capability.

- [ ] **Step 1: Confirm all non-NuGet external P10 prerequisites**

Record reviewed evidence that the downstream cosign key, pinned cosign trust,
R2 authority/write policy, and permanent read-only consumer credentials are
ready. If any is absent, keep `S04_EXTERNAL_PREREQUISITES_READY` unset and stop.

- [ ] **Step 2: Create and protect the Environment without Secrets**

Configure `p10-formal-release` with main-only deployment branch policy and one
required reviewer. Then read it back:

```powershell
gh api /repos/GTX537/CP6.Platform/environments/p10-formal-release --jq '{name:.name,protection_rules:.protection_rules,deployment_branch_policy:.deployment_branch_policy}'
```

Expected: the exact Environment, a reviewer rule, and main-only policy. A 404,
403, 422, ignored rule, or missing reviewer is `Candidate / No-Go`. Do not write
Secrets and do not substitute repository-level Secrets.

- [ ] **Step 3: Preflight DigiCert RFC3161 from both runner images**

Run a non-secret manual diagnostic workflow or temporary local request that
submits a SHA-256 RFC3161 query from `windows-2025` and `ubuntu-latest`, then
validates the returned token and normal TSA chain. Record the policy OID and
chain fingerprints. Both must pass; synthetic timestamps are forbidden.

## Task 11: Bootstrap and merge identical public trust into three repositories

**Files:**

- Platform: `eng/p10/trust/certificates/{certificateSha256}.cer`
- Platform: `eng/p10/trust/p10-formal-nuget-trust-store.v1.json`
- CRM: the same two relative paths under `D:\CP6.CRM`
- Public CP6: the same two relative paths under `D:\CP6`

- [ ] **Step 1: Create clean trust-bootstrap worktrees**

```powershell
git -C D:\CP6\CP6.Platform fetch origin main --prune
git -C D:\CP6\CP6.Platform worktree add -b codex/p10-s04-formal-trust-bootstrap D:\CP6.Platform-worktrees\p10-s04-formal-trust-bootstrap origin/main
git -C D:\CP6.CRM fetch origin main --prune
git -C D:\CP6.CRM worktree add -b codex/p10-s05-nuget-trust-bootstrap D:\CP6.CRM-worktrees\p10-s05-nuget-trust-bootstrap origin/main
git -C D:\CP6 fetch origin main --prune
git -C D:\CP6 worktree add -b codex/p10-s06-nuget-trust-bootstrap D:\CP6-worktrees\p10-s06-nuget-trust-bootstrap origin/main
```

Expected: three clean branches from their respective latest `origin/main`.

- [ ] **Step 2: Run the audited bootstrap exactly once**

From the Platform trust worktree, after the Environment is fully protected:

```powershell
pwsh -NoProfile -File ./eng/p10/Initialize-P10FormalCertificate.ps1 -Repository GTX537/CP6.Platform -Environment p10-formal-release
```

Expected: two confirmed Environment Secret names, one public CER, one canonical
trust JSON, and no local private material. Capture the printed public
fingerprint, SPKI ID, policy hash, validity, and certificate path.

- [ ] **Step 3: Verify zero private residue before copying public files**

```powershell
Get-ChildItem . -Recurse -File | Where-Object { $_.Extension -in '.pfx','.p12','.pem','.key' -or $_.Name -match '(?i)password|private[-_]?key' }
dotnet run --project tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj --configuration Release -- validate-nuget-trust eng/p10/trust/p10-formal-nuget-trust-store.v1.json eng/p10/trust/certificates
```

Expected: the residue command returns nothing and trust validation succeeds.

- [ ] **Step 4: Copy only the two public assets to clean downstream worktrees**

Create the two destination directories with `New-Item`; use `Copy-Item
-LiteralPath` for the resolved CER and JSON. Compute SHA-256 in all three
worktrees:

```powershell
$cer = Get-ChildItem eng/p10/trust/certificates -Filter '*.cer' -File -ErrorAction Stop
if ($cer.Count -ne 1) { throw 'Expected exactly one current public certificate.' }
Get-FileHash -Algorithm SHA256 -LiteralPath $cer.FullName
Get-FileHash -Algorithm SHA256 eng/p10/trust/p10-formal-nuget-trust-store.v1.json
```

Expected: the CER and JSON hashes are byte-identical across Platform, CRM, and
public CP6.

- [ ] **Step 5: Commit, review, and merge all three public trust PRs**

Stage only the two trust assets in each worktree. Use these commits:

```text
chore(release): pin P10 formal NuGet signer
```

Open three PRs, run repository-specific required checks, inspect each complete
diff, and merge only after all pass. Re-fetch all three `origin/main` SHAs and
prove the same public hashes are present on each remote main.

- [ ] **Step 6: Revalidate the Environment PFX against the merged Platform trust**

Run the secret-bearing preflight under `p10-formal-release`; it must prove the
PFX subject, DER fingerprint, SPKI, extensions, and validity exactly match the
public trust now on Platform main. Do not expose or download the Secret.

## Task 12: Dispatch S04 once and record immutable evidence

**External state:** GitHub Actions and GitHub Packages.

- [ ] **Step 1: Recheck that `0.10.0` is absent for all seven IDs**

Run `Test-P10FormalPrerequisites.ps1` against the exact current Platform main
and all three merged trust SHAs. Expected: all seven absent, trust hashes equal,
Environment protected, RFC3161 healthy, and external prerequisite flag true.

- [ ] **Step 2: Dispatch exact main**

```powershell
$sourceSha = gh api repos/GTX537/CP6.Platform/commits/main --jq .sha
gh workflow run p10-formal-packages.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$sourceSha -f version=0.10.0
```

Approve only the waiting `p10-formal-release` job after checking the displayed
SHA/version. Do not approve any different identity.

- [ ] **Step 3: Wait for both OS jobs and capture the exact run**

```powershell
gh run list --repo GTX537/CP6.Platform --workflow p10-formal-packages.yml --limit 1 --json databaseId,headSha,status,conclusion,attempt
$runId = gh run list --repo GTX537/CP6.Platform --workflow p10-formal-packages.yml --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $runId --repo GTX537/CP6.Platform --exit-status
```

Expected: exact source SHA, attempt, Windows conclusion `success`, Linux
conclusion `success`. Any post-upload failure burns `0.10.0` and remains No-Go.

- [ ] **Step 4: Independently download and validate final public evidence**

Download the final evidence artifact by exact run ID/name into a clean
directory. Validate canonical publication JSON, all seven feed downloads, raw
hash equality, trust policy, signature, timestamp, package identity/version,
and source SHA on Linux or a second clean environment.

- [ ] **Step 5: Update Platform status through a separate audited docs PR**

Record in `docs/P10-RELEASE-GOVERNANCE.md`: Platform main SHA, workflow SHA,
run ID/attempt, formal version, trust-policy version/hash, certificate DER
fingerprint and SPKI ID, timestamp policy OID/chain, seven package URLs and
dual hashes, artifact IDs/digests, and Windows/Linux conclusions. State:

```text
P10 S04: complete
P10 overall: Candidate / No-Go pending S05 and S06
publicCaTrusted: false
deployment: not authorized
```

Run documentation checks, review the diff, merge, and verify remote main.

## Final acceptance checklist

- [ ] Design and implementation PRs are merged to Platform `main`.
- [ ] No PFX/private key/password exists in Git, artifacts, logs, or retained
  runner paths.
- [ ] The Environment is protected and contains exactly the two named
  certificate Secrets.
- [ ] Platform, CRM, and public CP6 main branches pin identical CER and trust
  bytes before publication.
- [ ] Exactly seven `0.10.0` runtime packages were built once, signed by the
  Current pinned signer, and timestamped once through DigiCert RFC3161.
- [ ] GitHub Packages read-back bytes equal the pre-upload signed bytes for all
  seven packages.
- [ ] Windows and Linux formal verification both pass under the same trust
  policy.
- [ ] Formal publication evidence is canonical, complete, immutable, public,
  and secret-free.
- [ ] `CP6.Platform.Testing`, S02 signer, `TestOnlyNone`, synthetic timestamps,
  mixed versions, and public-CA claims are absent.
- [ ] Status remains non-deployable and not `Frozen / Consumable` until S05 and
  S06 complete.

## Execution handoff

The normal skill offers either subagent-driven execution or inline execution.
This project has already selected **inline execution in the current task**, so
use `superpowers:executing-plans` and complete the checkboxes sequentially. Do
not create subagents unless the user later changes that choice explicitly.
