# P10 Pinned Self-Signed Formal NuGet Trust Design

**Status:** Written design; awaiting final user review

**Date:** 2026-09-02

**Amends:** `2026-09-01-p10-release-governance-design.md`

**Scope:** P10 S04 formal NuGet author signing and the S05-S06 trust bootstrap

**Deployment status:** Not authorized

## 1. Decision

P10 uses one stable, self-signed X.509 leaf certificate as its initial formal
NuGet author-signing identity. The certificate is trusted only through an
out-of-band, reviewed SHA-256 fingerprint pin. It is not a public-CA identity.

The normative trust claims are:

- `trustModel=PinnedSelfSigned`;
- `publicCaTrusted=false`;
- `internallyTrusted=true`; and
- `timestampPolicy=Rfc3161Required`.

When every P10 gate succeeds, packages signed under this policy may contribute
to `Frozen / Consumable`. No evidence may describe this certificate as publicly
trusted, CA-issued, or equivalent to a public-CA certificate.

This amendment supersedes only the original design's assumptions about the
formal NuGet author certificate and its revocation source. The GitHub Packages,
cosign, R2, immutable-version, Locator, non-deployable Platform candidate, and
cross-repository completion rules remain unchanged.

## 2. Goals and non-goals

This design must:

1. remove the requirement to purchase a public code-signing certificate;
2. retain an X.509 author signature, real RFC3161 timestamp, immutable package
   bytes, and exact fingerprint verification;
3. keep the S02 test certificate incapable of satisfying formal validation;
4. allow Windows and Linux consumers to verify the same downloaded package
   bytes under one pinned policy;
5. keep the private key out of Git, workflow artifacts, logs, developer-machine
   files, retained runner files, and ordinary CI jobs; and
6. define low-overhead rotation and explicit revocation without operating a
   private CA, CRL, or OCSP service.

This design does not:

- create public-CA trust or Windows publisher reputation;
- relax the RFC3161 timestamp requirement;
- reuse the S02 certificate or test package transport;
- authorize deployment or a deployable System candidate;
- change the cosign release-key policy;
- create a local or cloud Windows VM; or
- permit trust-on-first-use from a package, candidate, Locator, or workflow
  output.

## 3. Formal certificate profile

The bootstrap process creates exactly one certificate with this profile:

| Field | Required value |
|---|---|
| Subject | `CN=CP6 Platform Release Signing` |
| Issuer | identical to Subject |
| Public-key algorithm | RSA |
| RSA key size | 3072 bits |
| Signature algorithm | SHA-256 with RSA |
| Basic Constraints | `CA=false`, critical |
| Key Usage | `DigitalSignature`, critical |
| Extended Key Usage | `1.3.6.1.5.5.7.3.3` code signing |
| Subject Key Identifier | required |
| Valid from | bootstrap UTC time minus five minutes |
| Valid until | bootstrap UTC time plus 730 days |
| Certificate fingerprint | lowercase SHA-256 of DER certificate bytes |
| SPKI identifier | `sha256:` plus lowercase SHA-256 of DER SubjectPublicKeyInfo |

The serial number is a positive cryptographically random 128-bit value. The PFX
password is the base64 encoding of 32 cryptographically random bytes. Neither
value may be supplied by a repository default or a predictable seed.

This certificate differs from the S02 test certificate in subject, key size,
lifetime, persistence, trust contract, timestamp policy, and allowed status.
Formal validation rejects `CN=CP6 Platform P10 TEST ONLY`, every S02 fingerprint,
`testOnly=true`, and `timestampPolicy=TestOnlyNone`.

## 4. Private-key bootstrap and storage

The protected GitHub Environment is named `p10-formal-release`. It holds exactly
these certificate Secrets:

- `P10_NUGET_SIGNING_PFX_BASE64`;
- `P10_NUGET_SIGNING_PFX_PASSWORD`.

An audited bootstrap command creates the certificate and PFX in process memory,
writes the two Secret values to GitHub through standard input, exports only the
public DER certificate, and clears private-key and password state in `finally`.
The command fails if the Environment does not exist or either Secret write is
not confirmed. It never prints either Secret, places Secret values in command
arguments, or writes a PFX to a local file.

GitHub Environment Secrets are the only retained copy of the private key and
password. There is intentionally no developer-workstation, VM, repository, or
artifact backup. Loss of either Secret requires rotation; it does not invalidate
already timestamped packages.

The Environment requires manual approval for the formal publication job. Its
Secrets are unavailable to pull-request workflows and to jobs that do not name
`p10-formal-release`.

## 5. Pinned NuGet trust contract

`CP6.Platform.Release` adds the supporting contract
`pinned-nuget-trust-store.v1`. Its schema and validator require:

- a positive `policyVersion`;
- `trustModel=PinnedSelfSigned`;
- `publicCaTrusted=false` and `internallyTrusted=true`;
- `timestampPolicy=Rfc3161Required`;
- the exact timestamp service URI `http://timestamp.digicert.com`;
- the exact seven allowed Platform package IDs in ordinal order;
- one `Current` signer and zero or more `Historical` or `Revoked` signers;
- for each signer, the public certificate path, DER certificate SHA-256, SPKI
  SHA-256 identifier, exact subject and issuer, validity interval, status, and
  activation time; and
- `revokedAt` plus a non-empty reason for `Revoked`, and JSON `null` for both
  fields otherwise.

The exact allowed package set is:

1. `CP6.Platform.Abstractions`;
2. `CP6.Platform.AspNetCore`;
3. `CP6.Platform.Contracts`;
4. `CP6.Platform.Deployment`;
5. `CP6.Platform.EntityFramework`;
6. `CP6.Platform.Messaging`;
7. `CP6.Platform.Release`.

Public certificates and the trust instance use these Platform paths:

- `eng/p10/trust/certificates/`, where every CER filename is its lowercase DER
  SHA-256 followed by `.cer`;
- `eng/p10/trust/p10-formal-nuget-trust-store.v1.json`.

The trust-store JSON is canonicalized under `DeterministicJsonProfile.v1`. Each
certificate hash must equal both its content-addressed filename and the bytes of
that CER file. Historical and revoked CER files are retained and never
overwritten. A candidate or package may select only a signer already present in
a reviewed trust-store commit; embedded certificates and claimed fingerprints
never create trust.

Before S04 publication, CRM and public CP6 receive the same CER bytes and trust
policy through reviewed repository commits. Their committed hashes must equal
the Platform bootstrap evidence. S05 and S06 do not learn trust from downloaded
packages or the R2 Locator.

## 6. Timestamp trust

The author certificate is allowed to have an untrusted root only when its exact
DER fingerprint is present in the pinned NuGet trust store and its status is
allowed for the requested verification mode. That exception never applies to
the RFC3161 timestamp certificate.

Every formal package has exactly one RFC3161 timestamp obtained from
`http://timestamp.digicert.com` using SHA-256. Verification requires the
timestamp token signature, timestamp EKU, signing time, policy identifier,
certificate validity at timestamp time, revocation result, and a chain to the
normal supported Windows or .NET SDK timestamp root store. Missing, synthetic,
untrusted, or malformed timestamps fail formal validation.

The workflow records the returned timestamp-policy identifier and timestamp
certificate chain. A later service-policy or chain change is a new preflight
input and cannot be silently accepted by an existing allow-list.

## 7. S04 workflow and data flow

The formal workflow is `p10-formal-packages.yml`. It is manually dispatched on
`main` with an exact expected commit and a full stable SemVer. It runs the
signing job on GitHub-hosted `windows-2025` under `p10-formal-release` and grants
only `contents: read` and `packages: write` to `GITHUB_TOKEN`.

The workflow performs these steps in order:

1. prove the event ref, expected commit, checkout HEAD, and current repository
   `main` are identical;
2. validate the committed trust store and public CER, then prove the Environment
   PFX has the same subject, fingerprint, SPKI, extensions, and validity;
3. prove the requested version is a stable SemVer and does not exist for any of
   the seven package IDs in GitHub Packages;
4. capture the exact runner image, .NET SDK, NuGet client, source SHA, workflow
   SHA, and one build invocation ID;
5. restore and build once, then pack exactly the seven allowed `.nupkg` files
   from that build invocation;
6. decode the PFX only into a job-scoped temporary directory, author-sign every
   package with the pinned certificate, add the required RFC3161 timestamp, and
   verify all formal signing rules;
7. record each final author-signed SHA-256 and publish the seven packages to
   `https://nuget.pkg.github.com/GTX537/index.json`;
8. download the same seven ID/version subjects from that feed into a fresh
   directory and prove each downloaded SHA-256 equals its pre-upload final
   author-signed SHA-256;
9. upload only public package and evidence material for a Linux verification
   job, which independently verifies archive integrity, exact fingerprint,
   author signature, timestamp, package identity, version, source SHA, and
   seven-package completeness; and
10. clear PFX bytes, password state, temporary certificate material, NuGet
    credentials, and job directories in unconditional cleanup paths.

`CP6.Platform.Testing`, symbol-only substitutes, mixed versions, multiple source
SHAs, and multiple build invocation IDs invalidate the formal set. Symbol
packages may be retained as public build evidence but do not replace or expand
the required seven formal package identities.

The feed policy is byte-preserving. For each package,
`authorSignedPackageSha256` must equal `publishedPackageSha256`. Any differing
downloaded byte makes S04 `Candidate / No-Go` and requires a feed-policy design
review.

## 8. Immutable version and partial publication

S04 preflight selects the exact formal package version once. The workflow marks
that version consumed immediately before the first package upload. An existing
ID/version is a hard conflict.

If any upload succeeds and a later step fails, the version remains consumed.
Packages are not deleted, overwritten, unlisted as a repair, or republished.
The retry uses a new stable version and produces new evidence. A pre-upload
failure may reuse the proposed version only when the feed proves that none of
the seven package ID/version subjects exists.

## 9. Rotation and revocation

Rotation begins 60 days before the current certificate expires. It creates a
new certificate using the same profile, increments `policyVersion`, commits the
new public CER and signer entry to Platform, CRM, and public CP6, and completes
all trust-policy gates before the new certificate signs a package.

After cutover, the previous signer becomes `Historical`. Historical signers may
verify already published packages but may not sign a new version. Public
RFC3161 timestamps preserve cryptographic signing-time evidence after the author
certificate expires.

Suspected private-key disclosure changes the signer to `Revoked`, records
`revokedAt` and the reason, increments the trust policy, and rotates immediately.
A revoked candidate remains inspectable but cannot be newly consumed or used as
a rollback target. The repository trust policy, not CRL or OCSP, is the
authoritative revocation source for the self-signed author certificate.

## 10. Failure and status semantics

Formal signing fails closed for any of these conditions:

- missing or undecodable Environment Secrets;
- PFX/public-CER subject, fingerprint, SPKI, extension, key-size, or validity
  mismatch;
- S02 subject, S02 fingerprint, `testOnly=true`, or `TestOnlyNone`;
- absent, invalid, synthetic, untrusted, or policy-incompatible timestamp;
- an existing formal version, a mixed package set, or a partial package set;
- a package-byte change before or after publication;
- a private-key or password artifact discovered in files, artifacts, or logs;
- failure of either Windows or Linux verification; or
- any attempt to claim `publicCaTrusted=true` under `PinnedSelfSigned`.

Pre-upload failures keep the project at `Candidate / No-Go` without consuming a
clean feed version. Post-upload, cross-platform, or read-back failures keep the
same status and permanently consume the version.

The self-signed trust model can satisfy the NuGet portion of P10 completion.
`Frozen / Consumable` still requires S05 CRM verification, successful completed
S06 validation, R2 publication and post-commit confirmation, exact cross-repo
evidence, and every other condition in the original P10 design.

## 11. Test and acceptance strategy

Contract and validator tests cover:

- valid `PinnedSelfSigned` trust stores;
- exact allowed package IDs and one-current-signer cardinality;
- rejection of `publicCaTrusted=true`, unknown trust models, duplicate signers,
  malformed fingerprints, inconsistent status fields, and non-canonical bytes;
- distinct formal and S02 test-certificate policies; and
- current, historical, revoked, expired, and not-yet-valid signer behavior.

Certificate and script tests cover:

- exact RSA-3072 profile, extensions, subject, validity, DER hash, and SPKI hash;
- public CER files without private keys;
- no PFX, password, or private-key residue after success or injected failure;
- rejection of missing timestamp, wrong fingerprint, weak key, wrong EKU,
  expired certificate, test subject, and tampered package; and
- cleanup after signing, publication, download, and verification failures.

Workflow and package tests cover:

- exact-main dispatch and protected-Environment binding;
- one build invocation and exactly seven packages;
- stable unused version enforcement and partial-publication behavior;
- pre-upload and downloaded byte equality;
- independent Windows and Linux verification; and
- secret scanning of repository files, logs, artifacts, and generated evidence.

S04 acceptance evidence records the formal version, Platform source SHA,
workflow path and SHA, run ID and attempt, runner image, .NET and NuGet versions,
trust-policy version and hash, author certificate identity, timestamp identity,
seven package URLs and both hashes, publication result, and Windows/Linux gate
results. Evidence contains no secret or machine-specific path.

## 12. Delivery boundary

This design change is complete when the amended specification is reviewed and
committed. Implementation occurs in a separate task branch and follows TDD.
Implementation must not dispatch S04 or publish a formal version until the
protected Environment, pinned trust commits, RFC3161 preflight, GitHub Packages
policy, downstream cosign key, R2 authority policy, and permanent read-only
consumer credentials are all ready.
