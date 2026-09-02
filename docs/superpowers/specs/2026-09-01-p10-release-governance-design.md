# P10 Release Governance, Signed Packages, and Candidate Evidence Design

**Status:** Approved design; S00-S03 complete; S04-S06 not started

**Date:** 2026-09-01

**Amendment:** The formal NuGet author-certificate trust model is amended by
[`2026-09-02-p10-pinned-self-signed-trust-design.md`](./2026-09-02-p10-pinned-self-signed-trust-design.md).
Where the two documents differ on author-certificate issuance, storage,
revocation, or public trust, the amendment is authoritative.

**Primary producer:** `CP6.Platform`

**Consumers:** `CP6.CRM`, then the public `CP6` release repository

**Production deployment status:** Not authorized

## 1. Context

P01-P09 established the Platform package, contract, observability, and non-production deployment foundations. P10 closes the release-governance gap: it adds a release contract package, produces one coherent signed seven-package Platform candidate, proves that CRM can consume it, and lets the public CP6 release workflow publish a signed, content-addressed Platform candidate record.

P10 does **not** create a production release. A deployable System Manifest requires exact identities for all four repositories—`CP6`, `CP6.Platform`, `CP6.CRM`, and `CP6.Portal`—plus their compatible images, packages, schemas, migrations, and evidence. Those prerequisites do not all exist. P10 therefore defines and validates the production contract while publishing only a separate Platform reference candidate with `candidateKind=PlatformReference` and `deployable=false`.

The accepted R00 design assumed S3 `VersionId` and Object Lock semantics for every consumed evidence object. Cloudflare R2 supports conditional `PutObject` requests but does not implement S3 Object Versioning or S3 Object Lock. P10 adopts a lighter Cloudflare-native model: content-addressed objects, single-request create-only writes, SHA-256 verification, and a signed Locator. The public CP6 repository must record this as a narrow R00 erratum during S06; it must not continue claiming unsupported `VersionId` or Object Lock guarantees.

Authoritative references:

- Cloudflare R2 S3 compatibility: <https://developers.cloudflare.com/r2/api/s3/api/>
- Cloudflare R2 upload behavior: <https://developers.cloudflare.com/r2/objects/upload-objects/>
- NuGet signed packages: <https://learn.microsoft.com/nuget/reference/signed-packages-reference>
- `dotnet nuget sign`: <https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-sign>
- Sigstore blob bundles: <https://docs.sigstore.dev/cosign/signing/signing_with_blobs/>
- GitHub Actions artifact identity and retention: <https://github.com/actions/upload-artifact>

## 2. Goals

P10 must:

1. add `CP6.Platform.Release` as the seventh approved Platform package;
2. define strict schemas and validators for production System Manifests and P10 Platform candidates;
3. build all seven packages from one exact `CP6.Platform` Git SHA and one build invocation;
4. sign formal NuGet packages with an X.509 code-signing certificate and RFC3161 timestamp;
5. prove CRM can consume the exact seven-package set without source references or runtime activation;
6. publish a signed, non-deployable Platform candidate through the authoritative GitHub R2/GHCR release workflow;
7. make incomplete, conflicting, test-only, revoked, or tampered candidates fail closed;
8. provide deterministic, cross-process JSON bytes for hashing and signing;
9. make the final Locator the only authoritative discovery commit point; and
10. leave auditable evidence without requiring storage-layer versioning or Object Lock.

## 3. Non-goals

P10 does not:

- publish a deployable four-repository System Manifest;
- fabricate a Portal SHA, image, migration, or compatibility result;
- deploy DEV, UAT, or PROD;
- change the selected container Registry away from GHCR;
- change the sole Platform NuGet authority away from GitHub Packages;
- make Azure Pipelines a release authority;
- add CRM runtime registrations, routes, workers, subscriptions, or `Program.cs` behavior;
- use `ProjectReference` or copied schemas as a substitute for consuming a released package;
- accept a test certificate, synthetic timestamp, mock R2 result, or unsigned OCI artifact as production evidence;
- depend on GitHub artifact attestations for P10 trust; or
- add heavy storage infrastructure solely to emulate unsupported S3 features.

## 4. Chosen architecture

P10 uses a **Platform producer-first** sequence.

- `CP6.Platform` owns the schemas, validators, fixtures, signing policy, deterministic JSON profile, the new Release package, and the unified seven-package build.
- `CP6.CRM` is a test-only consumer. It restores exact package versions and produces consumer evidence without enabling new runtime behavior.
- The public `CP6` repository remains the only authoritative GitHub R2/GHCR candidate workflow. It consumes a pinned `CP6.Platform.Release` package in a small verifier and does not copy Platform schemas.
- `CP6.Portal` remains unchanged. Its absence from the P10 candidate is explicit and is one reason the candidate is non-deployable.
- Every S00-S06 stage modifies one repository only. Cross-repository state is consumed through immutable commits, packages, workflow artifacts, and evidence locators rather than uncommitted files.

Rejected alternatives were:

1. **Define the contracts in the public release repository.** This makes the consumer the contract owner and encourages schema copies in Platform and CRM.
2. **Publish a nominal four-repository candidate now.** This would invent missing Portal and system compatibility evidence.
3. **Require a different object store before any P10 work.** This adds infrastructure weight without improving the P10 reference-candidate boundary.
4. **Create a second NuGet feed for S02.** The repository already fixes GitHub Packages as its sole package authority; test packages instead move through an immutable workflow artifact and a temporary local source.
5. **Require separate OCI and Locator keys in P10.** The approved operational boundary uses one dedicated release-signing key. The trust model scopes that key to both purposes and supports later purpose-specific keys without changing candidate schemas.

## 5. Package and feed boundary

The P10 candidate contains exactly these seven packages:

1. `CP6.Platform.Contracts`
2. `CP6.Platform.Abstractions`
3. `CP6.Platform.AspNetCore`
4. `CP6.Platform.Messaging`
5. `CP6.Platform.EntityFramework`
6. `CP6.Platform.Deployment`
7. `CP6.Platform.Release`

All seven packages must have one immutable release version, one source Git SHA, and one build invocation. A package from a different version, source SHA, or build invocation invalidates the set. The exact formal version is selected once during S04 preflight and is never reused after any partial upload.

`CP6.Platform.Release` contains public contract models, schemas, deterministic serialization support, validation entry points, and embedded positive/negative fixtures. It contains no private key, environment credential, deployment command, trust-policy selection, or production secret.

### 5.1 Formal feed identity

The sole P10 formal destination is:

- feed identity: `github-packages:GTX537`;
- service index: `https://nuget.pkg.github.com/GTX537/index.json`;
- expected transformation: `BytePreserving`;
- overwrite rule: an existing package ID/version is a hard conflict;
- delete/unlist rule: deletion is not a release repair mechanism, and the publication token must not be granted a package-delete capability.

P10 does not assume nuget.org repository countersigning semantics. Every package record nevertheless carries both identities:

- `authorSignedPackageSha256`: hash of the final author-signed and timestamped `.nupkg` before upload;
- `publishedPackageSha256`: hash of bytes downloaded from GitHub Packages;
- `feedTransformation`: `BytePreserving` for P10;
- author certificate fingerprint and timestamp-policy identifier;
- repository certificate fingerprint, which is null for `BytePreserving`; and
- exact feed identity and package version identity.

For P10, the two hashes must be equal. If GitHub Packages changes the bytes, S04 is `No-Go` and the feed transformation policy must be redesigned before publication continues. A consumer always verifies the downloaded `publishedPackageSha256` and the retained author signature.

### 5.2 Formal signing preflight

S04 preflight freezes and records:

- GitHub Packages feed identity and service index;
- package overwrite, deletion, restoration, and retention behavior;
- author-signing runner OS and runner image version;
- exact .NET SDK and NuGet client versions;
- X.509 certificate chain, EKU, fingerprint, validity, and revocation result;
- RFC3161 TSA URI, chain, EKU, and allowed timestamp policy;
- cross-platform signature verification on the supported Windows and Linux consumers; and
- the immutable formal package version.

For the initial P10 formal signer, the author-certificate chain is the single
leaf permitted by the reviewed `PinnedSelfSigned` trust policy. Its revocation
source is that versioned policy rather than CRL or OCSP. This exception applies
only to the exact pinned author-certificate fingerprint; the RFC3161 timestamp
chain must still validate through the normal supported timestamp trust store.

The signer may use `dotnet nuget sign`; P10 does not impose a Windows-only `nuget.exe` rule that applies to a different feed policy.

## 6. `DeterministicJsonProfile.v1`

All CP6-authored control objects use one custom deterministic profile. Schema validation is necessary but not sufficient; the raw JSON reader enforces this profile before model binding.

### 6.1 Byte rules

- UTF-8 without BOM;
- no insignificant whitespace and no trailing newline;
- exactly one JSON root object;
- object member names sorted with .NET `StringComparer.Ordinal` after NFC validation;
- strings must already be Unicode NFC and may not contain unpaired surrogates;
- `"`, `\`, and U+0000-U+001F are escaped deterministically; control characters use lowercase `\u00xx` escapes;
- other Unicode scalar values are emitted directly as UTF-8;
- comments and trailing commas are rejected;
- duplicate member names are rejected case-sensitively by the raw token reader before deserialization;
- timestamps are exactly `yyyy-MM-dd'T'HH:mm:ss.fff'Z'`; offsets, missing milliseconds, and additional fractional digits are rejected;
- optional absent fields are omitted; required nullable fields are present as JSON `null`;
- numbers are non-negative base-10 integers without a sign, leading zero, fraction, or exponent; and
- property names and enum tokens are case-sensitive.

### 6.2 Collection rules

Arrays that represent sets are sorted by a schema-defined ordinal key:

- repositories by repository name;
- packages by package ID;
- OCI images by repository, then digest;
- evidence records by evidence kind, subject kind, subject digest, then object key;
- trust keys by purpose, then key ID; and
- policy authorities by authority ID.

Arrays that represent an ordered process, especially database migrations and lineage, preserve declared order and are not resorted.

### 6.3 Resource limits

Control-object validators enforce:

- maximum canonical object size: 4 MiB;
- maximum nesting depth: 32;
- maximum members per object: 256;
- maximum entries per array: 4096;
- maximum UTF-8 byte length per string: 65,536; and
- maximum integer value: signed 64-bit maximum.

The Platform package includes golden-byte fixtures. Windows and Linux test processes must produce byte-identical output, reject duplicate properties before model binding, and reproduce the same SHA-256 values.

## 7. Contract model

P10 has four primary candidate contracts and supporting evidence/policy contracts. Unknown properties fail validation. Signatures cover exact canonical bytes; consumers do not parse and reserialize before signature verification.

### 7.1 `system-release-manifest.v1.json`

This is the production-only compatibility root. It requires:

- `candidateKind=System` and `deployable=true`;
- the exact Git SHA for `CP6`, `CP6.Platform`, `CP6.CRM`, and `CP6.Portal`;
- exact NuGet package identities and published SHA-256 values;
- exact OCI repository digests and signature evidence;
- OpenAPI and event-schema identities;
- Dapr component identities;
- ordered database migration identifiers;
- subject-bound SBOM, scan, provenance, gate, and verification evidence;
- compatibility conclusions for the complete system; and
- lineage to the previous System Manifest digest.

Lineage is mandatory. The first manifest may use `lineageMode=Bootstrap` with a null previous digest only when a valid `system-lineage-bootstrap-evidence.v1` object is referenced. Subsequent manifests use `lineageMode=Successor` and a non-null previous digest.

The manifest never contains its own object key, object hash, or signature. `ValidateSystemCandidate` requires the complete four-repository set and the consistent pair `candidateKind=System` plus `deployable=true`. It rejects Platform candidates, missing compatibility inputs, synthetic identities, test trust roots, and kind/boolean mismatches.

### 7.2 `candidate-result.v2.json`

This is the result envelope for a real system candidate. It binds:

- the protected release tag;
- the exact four repository SHAs;
- the System Manifest object reference and SHA-256;
- a completed `release-gate-result.v1` whose conclusion is `Success`;
- the validation workflow repository, path, workflow-file SHA, run ID, run attempt, commit SHA, and conclusion; and
- the required trust and evidence-policy versions.

It does not contain its own object identity or signature and cannot point to a Platform-only candidate. It does **not** claim the final conclusion of the publication workflow that has not yet completed. P10 implements and tests this contract but does not publish a successful instance.

### 7.3 `candidate-locator.v1.json`

The Locator is the signed authoritative discovery root. Its subject is discriminated:

- a production system lane targets `candidate-result.v2`; or
- the P10 Platform lane targets `platform-release-candidate.v1`.

The two lanes use separate validation entry points and cannot be substituted. The Locator binds the release tag, subject kind, exact subject object reference, subject SHA-256, trust-policy version, signer key ID, and `createdAtUtc`. It contains neither its own key/hash nor its signature bundle hash.

The Platform release tag must match this exact safe grammar and therefore cannot contain `/`, `\`, whitespace, or `..` path segments:

```text
^v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$
```

The keys are fixed:

```text
candidates/platform/<release-tag>/candidate-locator.v1.json
candidates/platform/<release-tag>/candidate-locator.v1.sigstore.json
```

The bundle key is derived locally from the trusted discovery path, not read from an unverified Locator. `createdAtUtc` equals the already-frozen subject `createdAtUtc`; a retry never reads the current clock to regenerate Locator bytes.

The exact Locator bytes are signed with `cosign sign-blob --bundle`. Consumers use the bundle and pinned public key with `cosign verify-blob`, then fetch the signed subject reference.

### 7.4 `platform-release-candidate.v1.json`

This is P10's only successful candidate payload. It contains:

- `candidateKind=PlatformReference` and `deployable=false`;
- the exact `CP6.Platform` source SHA and protected source reference;
- all seven package IDs, versions, author-signed hashes, published hashes, feed identities, author-signature details, and RFC3161 results;
- one provenance statement binding the seven pre-sign build outputs to one `buildInvocationId` and mapping them to the seven final signed package subjects;
- OCI/GHCR proof expressed as repository digests rather than mutable tags;
- subject-bound SBOM, scan, provenance, gate, and consumer-evidence references;
- the CRM consumer commit and verification result;
- publisher identity: CP6 repository SHA, workflow path, workflow-file SHA, run ID, attempt, and protected environment;
- verifier identity: exact `CP6.Platform.Release` version, published package hash, author signer fingerprint, and validator policy version;
- the completed validation workflow identity and `release-gate-result.v1` reference; and
- the applicable trust, storage-authority, signing, and evidence-policy versions.

It cannot contain placeholder identities for CP6, CRM, or Portal. `ValidatePlatformCandidate` requires the exact seven-package set and the consistent pair `candidateKind=PlatformReference` plus `deployable=false`. `ValidateSystemCandidate` rejects this object even when all Platform evidence is valid.

### 7.5 Supporting contracts

`CP6.Platform.Release` also owns:

- `release-gate-result.v1.json`: immutable output of a completed validation workflow, including exact run identity, all gate conclusions, input subject hashes, and overall `Success` or `Failure`;
- `system-lineage-bootstrap-evidence.v1.json`: separately signed authority, reason, trust-policy version, and initial-lineage subject;
- `evidence-record.v1.json`: evidence kind, producer workflow, policy version, access class, object reference, and one or more exact subjects;
- `build-invocation-provenance.v1.json`: source SHA, build invocation ID, toolchain identity, pre-sign outputs, and mappings to final package identities;
- `test-package-transport.v1.json`: exact S02 source/run identity and the post-upload GitHub Actions package artifact ID, digest, creation time, and expiry time; and
- `pinned-trust-store.v1.json`: storage-authority mappings, trusted key metadata, purpose, validity, and revocation state.

Every SBOM, scan, provenance, gate, and consumer result must bind back to exact Git SHAs, package hashes or OCI digests, producer workflow identity, and policy version. A free-floating evidence link is invalid.

## 8. R2 storage authority and object references

Public candidate objects do not carry mutable URLs, credentials, account-selected endpoints, or presigned URLs. An object reference is exactly:

```json
{
  "storageAuthority": "cp6-release-r2-v1",
  "key": "objects/sha256/ab/abcdef.../platform-release-candidate.v1.json",
  "mediaType": "application/vnd.cp6.platform-release-candidate.v1+json",
  "sha256": "abcdef...",
  "byteLength": 12345
}
```

The locally pinned trust store maps `cp6-release-r2-v1` to one provider, account ID, jurisdiction, endpoint template, bucket, allowed prefixes, access mode, and maximum object size. A Locator cannot redefine that mapping.

Before Locator signature verification, the verifier may access only:

1. the known Locator key under the preconfigured storage authority; and
2. the fixed adjacent bundle key.

It may parse only bounded selector fields needed to choose an already-pinned key. It must not follow an unverified bucket, key, endpoint, or URL. Subject and evidence references are followed only after the Locator signature succeeds.

All CP6 control objects are below 4 MiB and use one `PutObject` request. SDK transfer-manager or CLI behavior that silently switches these writes to multipart is forbidden. Each create uses `If-None-Match: *`. Content-addressed evidence keys derive from SHA-256.

The pinned storage policy also requires:

- no automatic lifecycle deletion for `candidates/`, `objects/`, or required evidence prefixes;
- no delete operation in the publication workflow or its normal credential path;
- a separate, audited administrative recovery identity if deletion is ever required;
- exact media type and byte-length checks on read; and
- no claim of `VersionId`, Object Lock, or delete-marker uniqueness.

## 9. Signing and trust bootstrap

P10 uses independent NuGet and cosign trust domains. The approved P10 cosign release key may serve the two purposes `oci` and `candidate-locator`; purpose metadata is mandatory, and schemas permit later separate keys.

### 9.1 NuGet trust

Formal packages are author-signed using the X.509 code-signing certificate
allowed by the reviewed NuGet trust policy, then timestamped by an approved
RFC3161 service. The initial policy is `PinnedSelfSigned`: the certificate is a
stable self-signed leaf, `publicCaTrusted=false`, and `internallyTrusted=true`.
Its exact DER fingerprint is pinned out of band in Platform, CRM, and public CP6.
The formal workflow verifies the signature, certificate policy, timestamp,
package metadata, and both package hashes before and after upload. This policy
may contribute to `Frozen / Consumable`, but it never represents public-CA
trust.

S00-S03 may use a repository test certificate. Every resulting artifact and package carries `testOnly=true`; formal validation rejects the test certificate and missing or synthetic timestamps.

### 9.2 Cosign trust

The cosign private key is held by the protected `r2-candidate` environment or its isolated runner. Private keys never enter packages, repositories, logs, fixtures, workflow artifacts, or general-purpose CI agents.

`keyId` is `sha256:<lowercase SHA-256 of DER SubjectPublicKeyInfo>`. A pinned trust entry contains:

- policy version;
- key ID and public-key SHA-256;
- allowed purpose or purposes;
- `validFrom` and `validUntil`;
- optional `revokedAt` and mandatory revocation reason when revoked; and
- the trusted source commit or exact package hash that delivered the entry.

The trust store is obtained out of band from the Locator: the CP6 verifier pins it in a reviewed repository commit, while external consumers receive it through an already-trusted release package or configuration channel. The Locator's key ID is only a selector into that preexisting store. The current trust store defines the minimum accepted policy version and explicitly accepted historical versions; a Locator cannot select an older or unknown policy to bypass current revocation or purpose rules.

Rotation creates a new policy version and candidate. It never rewrites an old candidate.

Validation applies two revocation modes:

- **Historical audit:** report whether the signature was valid at signing time and separately report current revocation.
- **Current consumption, deployment, and rollback:** a currently revoked key blocks acceptance even when the signature was valid historically.

A revoked candidate becomes `Revoked`; it remains inspectable for audit but cannot be newly consumed or used as a rollback target.

## 10. S02-to-S03 test package transport

S02 does not publish test packages to GitHub Packages. It first uploads one GitHub Actions v4 package artifact named:

```text
p10-s02-packages-<platform-sha>-<run-attempt>
```

The package artifact contains:

- the seven `.nupkg` and seven `.snupkg` files;
- a canonical test package manifest;
- final file SHA-256 values;
- test certificate and `testOnly=true` evidence;
- exact Platform source SHA, run ID, run attempt, and build invocation ID; and
- the unique test package version and locked-restore metadata.

`overwrite` is forbidden. After the package upload, the workflow records the returned artifact ID and digest plus the API-reported creation and expiry times in `test-package-transport.v1.json`; this record is separate because an artifact cannot contain identifiers that exist only after its upload completes. The record is bound to the Platform workflow run and source SHA, then uploaded once as a second artifact named:

```text
p10-s02-transport-<platform-sha>-<run-attempt>
```

The handoff to CRM fixes the workflow run ID plus both artifact IDs and API-reported digests. Both artifacts use the maximum approved retention period, and S03 must finish before either expires.

S03 uses a least-privilege cross-repository token with Actions read access. It queries the artifact API and requires each returned ID, digest, source run, source SHA, expiry, and non-expired state to match the handoff. It downloads the raw transport ZIP by artifact ID, verifies its API digest before extraction, then validates the canonical transport record. It next downloads the raw package ZIP by the ID recorded in that trusted record, verifies its API digest before extraction, and independently verifies every contained file hash. A digest warning or mismatch is a hard failure.

CRM extracts the packages into an ephemeral local directory, creates a task-only NuGet source mapping for `CP6.Platform.*`, uses an exact test version and locked restore, and deletes the temporary source after verification. Test packages are never promoted to the formal feed. If the artifact expires before S03, S02 must create a new uniquely identified test artifact and S03 must rerun against it.

## 11. Staged delivery

| Stage | Repository | Deliverable |
|---|---|---|
| S00 | Platform | Close the P0 schema decisions; add the Release package boundary, deterministic JSON profile, primary contracts, supporting contracts, signing policy, and storage-authority model. |
| S01 | Platform | Implement strict validators, canonical serialization, raw duplicate-member rejection, trust policy, and positive/negative golden fixtures. |
| S02 | Platform | Build and verify one seven-package test candidate, upload its immutable test artifact, and do not publish it to GitHub Packages. |
| S03 | CRM | Download the exact S02 artifact, restore from a temporary source, prove compatibility, and keep runtime registration unchanged. |
| S04 | Platform | Build once, author-sign and timestamp the seven packages, publish them to GitHub Packages, and verify both pre-upload and downloaded identities. |
| S05 | CRM | Pin and verify the exact S04 version, signatures, hashes, and contracts; publish subject-bound consumer evidence. |
| S06 | Public CP6 | Pin `CP6.Platform.Release`, run validation and publication workflows, publish and cosign the non-deployable Platform candidate and Locator, record the R00 erratum, and complete the cross-repository audit. |

S04 may start only after preflight confirms that the formal NuGet certificate, RFC3161 service, downstream cosign key, pinned trust store, protected environment, GitHub Packages policy, and R2 authority policy are ready. Missing inputs produce `Candidate / No-Go`; test inputs never substitute.

## 12. Validation, publication, and commit protocol

S06 uses two workflows so a candidate never claims the unknown final conclusion of the workflow currently publishing it.

### 12.1 Completed validation workflow

The validation workflow:

1. pins exact Platform, CRM, and public CP6 commits;
2. verifies the seven downloaded formal packages, CRM consumer evidence, OCI digests, SBOM, scans, provenance, trust store, storage policy, and all required gates;
3. emits canonical `release-gate-result.v1` with a real final `Success` or `Failure` conclusion; and
4. completes before publication may start.

Only a successful immutable gate result can be consumed by publication.

### 12.2 Publication pre-commit

The publication workflow records its repository SHA, workflow path, workflow-file SHA, run ID, attempt, and environment, but never predicts its final conclusion. It then:

1. assembles and canonicalizes `platform-release-candidate.v1` from the completed gate result;
2. uploads the candidate and evidence to content-addressed keys;
3. constructs the intended Locator once, using the subject's frozen `createdAtUtc`;
4. signs those exact Locator bytes and creates the sigstore bundle;
5. conditionally uploads or reuses the fixed bundle key; and
6. runs a clean verifier before the Locator exists.

The clean verifier receives the intended Locator bytes through the current run's immutable job artifact and independently verifies:

- canonical bytes and schema limits;
- Locator signature and pinned trust policy;
- remote subject object and all required evidence hashes;
- package, OCI, provenance, producer, verifier, and policy bindings;
- candidate kind and `deployable=false`; and
- bundle and subject key derivation.

Failure here stops before the authoritative discovery commit.

### 12.3 Atomic commit

After successful pre-commit verification, the publication workflow performs exactly one final operation:

```text
PutObject(finalLocatorKey, exactLocatorBytes, If-None-Match: *)
```

This single request is the candidate's authoritative discovery commit point.

### 12.4 Post-commit confirmation

After the commit, a clean verifier downloads the Locator through the authoritative path and repeats signature, subject, evidence, kind, and hash verification. This is availability and storage confirmation, not a chance to rewrite the candidate.

The external audit state becomes:

- `Published-Unconfirmed` immediately after the successful Locator write;
- `Frozen` only after post-commit confirmation and the S06 cross-repository audit;
- `Rejected` if confirmation detects wrong or unavailable bytes; or
- `Revoked` if current trust policy later revokes the signing key.

If post-commit confirmation fails, the Locator remains immutable. The current release tag and candidate identity are burned; a correction uses a new release tag or candidate identity. No workflow overwrites the old Locator.

## 13. Bundle and Locator retry rules

The intended Locator bytes are generated once per publication identity and retained unchanged for all step-level retries. A whole workflow rerun has a new run attempt and must use a new candidate identity unless it consumes an explicitly preserved byte-identical publication intent.

Bundle handling is:

1. If the bundle key is absent, sign the intended Locator bytes and conditionally upload the bundle.
2. If a concurrent upload wins, or the bundle key already exists, download the existing bundle.
3. Verify that the existing bundle validates the exact intended Locator bytes with the pinned key and policy.
4. Reuse a valid existing bundle even if a newly generated bundle would differ byte-for-byte.
5. Treat an invalid bundle as `No-Go`; never overwrite it or blindly re-sign into the same key.

Locator handling is:

1. If create succeeds, enter `Published-Unconfirmed` and run post-commit confirmation.
2. If create returns a precondition conflict, download the existing Locator and fixed bundle.
3. Treat it as idempotent only when the Locator bytes are exactly the intended bytes and all signature/policy checks pass.
4. Any difference burns the candidate identity and is `No-Go`.

## 14. Failure, rollback, and state model

- A build, test, signing, timestamp, hash, scan, trust, or pre-commit validation failure stops before Locator publication.
- A partial NuGet upload leaves unreferenced packages. No Locator is generated, and the version is not reused.
- A partial R2 upload leaves content-addressed orphan objects. They may be visible through object listing or a known hash, but they must not be accepted through the authoritative discovery path as a complete candidate.
- A CRM or CP6 read-back failure prevents version pinning and completion-state updates.
- A wrong candidate kind, wrong schema, or `deployable=false` at a deployment entry point fails closed.
- A post-commit failure produces `Rejected`, not an in-place repair.
- Certificate or key revocation produces a new trust policy and candidate; historical objects are not overwritten.
- Rollback means selecting a previous candidate that still passes **current** trust and revocation policy. A `Rejected`, `Revoked`, or `Superseded` candidate is not a rollback target.

The project/audit state machine is:

```text
Draft -> TestOnly -> Validated -> Published-Unconfirmed -> Frozen -> Superseded
                    |                    |                 |
                    +-> Rejected <-------+                 +-> Revoked
```

`Candidate / No-Go` is a project decision, not an object mutation. `Frozen / Consumable` maps to `Frozen`. State transitions are recorded in append-only, content-addressed audit entries and project ledgers; they do not rewrite candidate bytes.

## 15. Access model

The Locator, bundle, Platform candidate, trust-policy identifier, package identities, sanitized gate result, and evidence hashes are public-readable control information unless repository policy explicitly requires authenticated read. They may not contain secrets, private customer data, machine paths, or raw tokens.

Evidence records declare one of:

- `RequiredPublic`: required for normal candidate verification and readable by every intended consumer;
- `RestrictedAudit`: raw logs or reports available only to authorized auditors; or
- `TestOnly`: S00-S03 evidence that formal validators reject.

A `RestrictedAudit` object cannot be the only evidence for a normal acceptance decision. A signed, sanitized `RequiredPublic` result must bind the same subjects and conclusion. CRM raw logs may remain private; their public consumer result contains only immutable identities, counts, conclusions, and safe diagnostics.

## 16. Verification strategy

### 16.1 Platform

- positive and negative tests for all primary and supporting schemas;
- missing, unknown, duplicate, malformed, over-limit, and wrong-kind property tests;
- strict four-repository System Manifest tests;
- `candidateKind` and `deployable` consistency tests;
- mutual rejection between System and Platform candidates;
- bootstrap and successor lineage tests;
- deterministic golden bytes on Windows and Linux;
- exact seven-package set, version, build invocation, and source-SHA tests;
- evidence subject-binding tests;
- test-certificate rejection by formal policy; and
- formal per-package X.509 and RFC3161 verification.

### 16.2 CRM

- exact seven-package restore from the S02 artifact's temporary source and later from GitHub Packages;
- locked restore with no `ProjectReference`;
- artifact, package, signature, and published-hash verification;
- build, unit, architecture, and contract gates;
- negative tests for missing packages, mixed versions, wrong hashes, test trust roots, and tampered candidates; and
- an architecture assertion that P10 adds no `Program.cs` runtime registration.

### 16.3 Public CP6

- pinned trust bootstrap before Locator field use;
- fixed Locator/bundle key derivation and bounded pre-signature parsing;
- cosign verification with the pinned public key;
- R2 authority, single-part conditional write, media type, byte length, and SHA-256 verification;
- bundle absent, valid-existing, invalid-existing, and concurrent-create tests;
- Locator first write, identical retry, conflicting write, and post-commit failure tests;
- validation/publication workflow temporal-separation tests;
- proof that Platform candidates are `deployable=false` and rejected by deployment validation;
- current-revocation, historical-audit, rollback, and rotation tests;
- regression checks showing that existing GitHub R2/GHCR, SBOM, scan, signature, and digest gates remain enabled; and
- secret-redaction and untrusted-output tests.

Synthetic certificates and mocked R2 calls are acceptable only for S00-S03 automated coverage. They are not formal acceptance evidence.

## 17. Completion and status rules

P10 becomes `Frozen / Consumable` only after:

1. the formal seven-package set is signed, timestamped, published to GitHub Packages, and read back with matching author-signed and published hashes;
2. CRM consumes and verifies that exact set;
3. a completed validation workflow publishes a successful `release-gate-result.v1`;
4. the public CP6 publication workflow passes pre-commit verification, conditionally writes the Locator, and passes post-commit confirmation;
5. all required gates pass on the relevant immutable commits; and
6. the public project-state documents and changelog record exact package versions, commit SHAs, object hashes, workflow identities, trust-policy versions, state, and remaining production boundary.

The initial formal author certificate may satisfy item 1 under the amended
`PinnedSelfSigned` policy only when the evidence records
`publicCaTrusted=false`, `internallyTrusted=true`, and the exact reviewed
fingerprint. No P10 state may imply public-CA trust for that certificate.

If any formal signing input, protected environment, publication destination, trust bootstrap, or real verification result is absent, the status remains `Candidate / No-Go`. Test certificates, synthetic data, skipped gates, partial package sets, expired test artifacts, or candidate-shaped fixtures cannot satisfy completion.

The public S06 update includes `docs/project-memory/PROJECT_STATE.md`, `05-Completed.md`, `06-Todo.md`, and `CHANGELOG-AI.md`. Platform and CRM update their own P10 publication and consumer ledgers in their respective stages.

## 18. P0 specification closure checklist

S00 implementation may not start until this document is reviewed and all rows are accepted:

| P0 item | Fixed decision |
|---|---|
| Final verification timing | Clean verification occurs before the Locator commit; post-commit confirmation has explicit immutable failure states. |
| Locator and bundle discovery | Fixed adjacent keys, stable creation time, and verify-before-reuse retry rules. |
| Exact JSON bytes | `DeterministicJsonProfile.v1`, raw duplicate rejection, limits, and cross-process golden fixtures. |
| R2 authority | Pinned `storageAuthority` mapping plus key, media type, hash, and byte length; single-part conditional writes. |
| Formal NuGet semantics | GitHub Packages fixed as authority; dual package hashes; P10 requires byte preservation. |
| S02 to S03 transport | Exact GitHub Actions v4 artifact ID/digest and temporary local NuGet source. |
| Workflow conclusion cycle | Completed validation gate result is consumed by a separate publication workflow; publication final conclusion is never predicted inside the candidate. |
| Trust bootstrap and revocation | Out-of-band pinned trust store, SPKI-derived key IDs, purpose/validity/revocation rules, and separate historical/current modes. |

After user acceptance, change the document status to `Approved design; P0 specification closure complete; implementation not started`, commit that approval, and only then invoke implementation planning.

## 19. Implementation boundary

This document approves design revision and planning only. Implementation must use independent task branches/worktrees, test-driven contract changes, exact file-level staging, full diff review, and the repository's standard verification gates. It does not authorize production deployment, force-push, shared-history rewrites, remote-branch deletion, package deletion, or weakening an existing R2 release gate.
