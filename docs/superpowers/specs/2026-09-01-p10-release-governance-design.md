# P10 Release Governance, Signed Packages, and Candidate Evidence Design

**Status:** Approved design; implementation not started

**Date:** 2026-09-01

**Primary producer:** `CP6.Platform`

**Consumers:** `CP6.CRM`, then the public `CP6` release repository

**Production deployment status:** Not authorized

## 1. Context

P01-P09 established the Platform package, contract, observability, and non-production deployment foundations. P10 closes the release-governance gap: it adds a release contract package, produces one coherent signed seven-package Platform candidate, proves that CRM can consume it, and lets the public CP6 release workflow publish a signed, content-addressed Platform candidate record.

P10 does **not** create a production release. A deployable System Manifest requires exact identities for all four repositories—`CP6`, `CP6.Platform`, `CP6.CRM`, and `CP6.Portal`—plus their compatible images, packages, schemas, migrations, and evidence. Those prerequisites do not all exist. P10 therefore defines and validates the production contract while publishing only a separate Platform reference candidate with `deployable=false`.

The accepted R00 design assumed S3 `VersionId` and Object Lock semantics for every consumed evidence object. Cloudflare R2 supports conditional `PutObject` requests but does not implement S3 Object Versioning or S3 Object Lock. P10 adopts a lighter Cloudflare-native model: content-addressed objects, create-only writes, SHA-256 verification, and a signed Locator. The public CP6 repository must record this as a narrow R00 erratum during S06; it must not continue claiming unsupported `VersionId` or Object Lock guarantees.

Reference: <https://developers.cloudflare.com/r2/api/s3/api/>

## 2. Goals

P10 must:

1. add `CP6.Platform.Release` as the seventh approved Platform package;
2. define strict schemas and validators for production System Manifests and P10 Platform candidates;
3. build all seven packages from one exact `CP6.Platform` Git SHA;
4. sign formal NuGet packages with an X.509 code-signing certificate and RFC3161 timestamp;
5. prove CRM can consume the exact seven-package set without source references or runtime activation;
6. publish a signed, non-deployable Platform candidate through the authoritative GitHub R2/GHCR release workflow;
7. make incomplete, conflicting, test-only, or tampered candidates fail closed; and
8. leave auditable evidence without requiring storage-layer versioning or Object Lock.

## 3. Non-goals

P10 does not:

- publish a deployable four-repository System Manifest;
- fabricate a Portal SHA, image, migration, or compatibility result;
- deploy DEV, UAT, or PROD;
- change the selected authoritative Registry away from GHCR;
- make Azure Pipelines a release authority;
- add CRM runtime registrations, routes, workers, subscriptions, or `Program.cs` behavior;
- use `ProjectReference` or copied schemas as a substitute for consuming a released package;
- accept a test certificate, synthetic timestamp, mock R2 result, or unsigned OCI artifact as production evidence; or
- add heavy storage infrastructure solely to emulate unsupported S3 features.

## 4. Chosen architecture

P10 uses a **Platform producer-first** sequence.

- `CP6.Platform` owns the schemas, validators, fixtures, signing policy, deterministic JSON rules, the new Release package, and the unified seven-package build.
- `CP6.CRM` is a test-only consumer. It restores exact package versions and produces consumer evidence without enabling new runtime behavior.
- The public `CP6` repository remains the only authoritative GitHub R2/GHCR candidate workflow. It consumes a pinned `CP6.Platform.Release` package in a small verifier and does not copy Platform schemas.
- `CP6.Portal` remains unchanged. Its absence from the P10 candidate is explicit and is one reason the candidate is non-deployable.
- Every S00-S06 stage modifies one repository only. Cross-repository state is consumed through immutable commits, packages, and evidence locators rather than uncommitted files.

Rejected alternatives were:

1. **Define the contracts in the public release repository.** This makes the consumer the contract owner and encourages schema copies in Platform and CRM.
2. **Publish a nominal four-repository candidate now.** This would invent missing Portal and system compatibility evidence.
3. **Require a different object store before any P10 work.** This adds infrastructure weight without improving the P10 reference-candidate boundary. Formal trust instead comes from signatures and verified content hashes.

## 5. Package boundary

The P10 candidate contains exactly these seven packages:

1. `CP6.Platform.Contracts`
2. `CP6.Platform.Abstractions`
3. `CP6.Platform.AspNetCore`
4. `CP6.Platform.Messaging`
5. `CP6.Platform.EntityFramework`
6. `CP6.Platform.Deployment`
7. `CP6.Platform.Release`

All seven packages must have one immutable release version, one source Git SHA, and one build invocation. A package from a different version or source SHA invalidates the set. The exact release version is selected once during implementation and publication; the design does not reserve or reuse a package version before the formal signing inputs are available.

`CP6.Platform.Release` contains the public contract models, schemas, deterministic serialization support, validation entry points, and embedded positive/negative fixtures needed by consumers. It contains no private key, environment credential, deployment command, or production secret.

## 6. Contract model

All contracts use UTF-8 JSON, closed objects, explicit schema versions, RFC3339 UTC timestamps, and lowercase hexadecimal SHA-256 values. Unknown properties fail validation. Signatures cover the exact serialized bytes; consumers must not parse and reserialize an object before verification.

### 6.1 `system-release-manifest.v1.json`

This is the production-only compatibility root. It requires:

- the exact Git SHA for `CP6`, `CP6.Platform`, `CP6.CRM`, and `CP6.Portal`;
- exact NuGet package identities and SHA-256 values;
- exact OCI repository digests and signature evidence;
- OpenAPI and event-schema identities;
- Dapr component identities;
- ordered database migration identifiers;
- SBOM, vulnerability-scan, provenance, and verification evidence references;
- compatibility conclusions for the complete system; and
- lineage to the previous System Manifest digest.

Lineage is mandatory. The first manifest in a lineage may use `lineageMode=Bootstrap` with a null previous digest only when separately signed bootstrap evidence is present. Subsequent manifests use `lineageMode=Successor` and a non-null previous digest.

The manifest never contains its own object key, object hash, or signature. Its identity is established by its consumer and by the outer candidate result.

`ValidateSystemCandidate` requires the complete four-repository set and rejects `deployable=false`, Platform-only candidates, missing compatibility inputs, synthetic identities, and test trust roots.

### 6.2 `candidate-result.v2.json`

This is the result envelope for a real system candidate. It binds:

- the protected release tag;
- the exact four repository SHAs;
- the System Manifest object reference;
- the System Manifest SHA-256;
- the release-workflow identity and conclusion; and
- the required evidence-policy version.

It does not contain its own object identity or signature and cannot point to a Platform-only candidate. P10 implements and tests this contract but does not publish a successful instance.

### 6.3 `candidate-locator.v1.json`

The Locator is the signed discovery root. Its subject is discriminated:

- a production system lane must target `candidate-result.v2`; or
- the P10 Platform lane must target `platform-release-candidate.v1`.

The two lanes use separate validation entry points and cannot be substituted for each other. The Locator binds the release tag, subject kind, exact subject object reference, subject SHA-256, trust-policy version, signer key identifier, and creation time. It does not contain its own signature identity.

The exact Locator bytes are signed with the dedicated cosign key. A detached cosign bundle is uploaded before the Locator, and the Locator is written last to its deterministic discovery key with `If-None-Match: *`. Consumers fetch the bundle associated with that key, verify the exact Locator bytes with the pinned public key, then fetch and hash the subject.

### 6.4 `platform-release-candidate.v1.json`

This is P10's only successful candidate payload. It contains:

- `deployable=false` and a Platform-specific candidate kind;
- the exact `CP6.Platform` source SHA and protected source reference;
- all seven package IDs, versions, package SHA-256 values, repository identities, author-signature information, and RFC3161 timestamp results;
- the OCI/GHCR proof available to the authoritative release workflow, expressed as repository digests rather than mutable tags;
- SBOM, provenance, scan, gate, and consumer-evidence references;
- the CRM consumer commit and verification result; and
- the applicable signing and evidence-policy versions.

It cannot contain placeholder identities for CP6, CRM, or Portal. `ValidatePlatformCandidate` requires the exact seven-package set and `deployable=false`. `ValidateSystemCandidate` rejects this object even when all its Platform evidence is valid.

## 7. Cloudflare R2 evidence model

An authoritative object reference contains:

- `bucket`;
- `key`;
- lowercase `sha256`; and
- byte length.

Evidence payloads use content-addressed keys derived from their SHA-256. They are uploaded with `If-None-Match: *`. A retry is successful only when the existing bytes have the expected hash; different existing bytes are a conflict.

The deterministic Locator discovery key is also create-only. Every read performs signature and SHA-256 verification, so a deleted object cannot be silently replaced with different content. Re-uploading identical bytes does not change the candidate's meaning.

P10 deliberately does not claim Cloudflare R2 `VersionId`, S3 Object Lock, delete-marker uniqueness, or storage-level retention. Operational retention may be added independently, but it is not part of the cryptographic candidate identity.

## 8. Signing and trust roots

P10 uses two explicit trust roots.

### 8.1 NuGet

Formal NuGet packages are author-signed using a user-provided X.509 code-signing certificate and private key, then timestamped by an approved RFC3161 service. The formal workflow verifies the signature, certificate policy, timestamp, package hash, and package metadata before and after upload.

S00-S03 may use a repository test certificate for automated negative and integration tests. Every resulting artifact must carry an unambiguous `testOnly` marker. Formal validation rejects the test certificate and any missing or synthetic timestamp.

### 8.2 OCI and Locator

OCI identities and the Candidate Locator use a dedicated cosign private key held by the protected `r2-candidate` environment or its isolated runner. The public key, key identifier, policy version, and rotation history are committed for consumers. A rotation creates a new policy version; it does not rewrite old candidates.

Private keys never enter packages, repositories, logs, test fixtures, workflow artifacts, or general-purpose CI agents.

## 9. Staged delivery

| Stage | Repository | Deliverable |
|---|---|---|
| S00 | Platform | Add the Release package boundary and define the four contracts, signing policy, and Cloudflare-native object reference model. |
| S01 | Platform | Implement strict validators, closed schemas, deterministic serialization, and positive/negative fixtures. |
| S02 | Platform | Build and verify one seven-package test candidate with clearly marked test trust roots; do not publish it to the formal feed. |
| S03 | CRM | Consume exact alpha/test packages, prove dependency and contract compatibility, and keep runtime registration unchanged. |
| S04 | Platform | Build the seven packages once from one SHA, sign them with the formal X.509/RFC3161 inputs, publish them, and verify each package from the feed. |
| S05 | CRM | Pin and verify the exact S04 package version, signatures, hashes, and contracts; publish consumer evidence. |
| S06 | Public CP6 | Pin `CP6.Platform.Release`, update the authoritative R2/GHCR verifier, publish and cosign the non-deployable Platform candidate and Locator, record the R00 erratum, and complete the cross-repository audit. |

S04 may start only after preflight confirms that the formal NuGet certificate, RFC3161 service, downstream cosign key, committed public key, protected environment, package destination, and R2 conditional-write configuration are ready. Platform S04 uses the NuGet trust inputs; the downstream cosign input is checked at the same milestone so that a signed package set is not presented as a complete P10 candidate when S06 cannot finish.

## 10. Publication flow

1. Resolve one protected Platform source reference to one exact Git SHA.
2. Build all seven packages once.
3. Run local gates, sign and timestamp all packages, compute hashes over the final signed `.nupkg` bytes, and verify their signatures and timestamps.
4. Publish the package set and read every package back from the feed for identity, hash, and signature verification.
5. CRM pins the exact version and publishes its consumer result.
6. The public CP6 workflow consumes the pinned Release package and exact CRM evidence.
7. It uploads content-addressed evidence and `platform-release-candidate.v1` objects.
8. It builds and cosign-signs the exact Locator bytes.
9. It uploads the detached signature bundle.
10. It conditionally writes the Locator last.
11. A clean verifier resolves the Locator, verifies its signature, verifies every referenced hash, and confirms that the candidate is `deployable=false`.

Only step 10 makes the candidate discoverable. No production deployment job may accept the Platform candidate kind.

## 11. Failure, idempotency, and rollback

- A build, test, signing, timestamp, hash, scan, or local validation failure stops before candidate publication.
- A partial NuGet upload leaves unreferenced packages. No Locator is generated, and the version is not reused.
- A partial R2 upload leaves content-addressed orphan objects. Without a Locator they are not a candidate.
- If a Locator create returns a precondition conflict, the workflow verifies the existing Locator and signature. Identical trusted bytes are an idempotent success; any difference is `No-Go`.
- A CRM or CP6 read-back failure prevents version pinning and completion-state updates.
- A wrong candidate kind, wrong schema, or `deployable=false` at a deployment entry point fails closed.
- Certificate or key revocation updates trust policy and requires a new package version and candidate. Historical objects are not overwritten.
- Rollback means pinning the previous verified candidate. It does not delete or mutate the failed or current candidate.

This provides the required guarantee: incomplete candidates are not discoverable as complete, and an existing candidate identity cannot silently change content.

## 12. Verification strategy

### 12.1 Platform

- positive and negative tests for all four schemas;
- missing, unknown, duplicate, malformed, and wrong-kind property tests;
- strict four-repository System Manifest tests;
- mutual rejection between System and Platform candidates;
- bootstrap and successor lineage tests;
- deterministic-byte and tamper-detection tests;
- exact seven-package set, version, and source-SHA tests;
- test-certificate rejection by the formal policy; and
- formal per-package X.509 and RFC3161 verification.

### 12.2 CRM

- exact seven-package restore with no `ProjectReference`;
- build, unit, architecture, and contract gates;
- negative tests for missing packages, mixed versions, wrong hashes, test trust roots, and tampered candidates; and
- an architecture assertion that P10 adds no `Program.cs` runtime registration.

### 12.3 Public CP6

- cosign Locator verification with the pinned public key;
- R2 read-back, byte-length, object-kind, and SHA-256 verification;
- first write, identical retry, and conflicting write tests;
- proof that the Platform candidate is `deployable=false` and rejected by deployment validation;
- regression checks showing that existing GitHub R2/GHCR, SBOM, scan, signature, and digest gates remain enabled; and
- secret-redaction and untrusted-output tests for workflow logs and evidence.

Synthetic certificates and mocked R2 calls are acceptable only for S00-S03 automated coverage. They are not formal acceptance evidence.

## 13. Completion and status rules

P10 becomes `Frozen / Consumable` only after:

1. the formal seven-package set is signed, timestamped, published, and read back;
2. CRM consumes and verifies that exact set;
3. the public CP6 workflow publishes and verifies the signed non-deployable Locator;
4. all required gates pass on the relevant immutable commits; and
5. the public project-state documents and changelog record the exact package version, commit SHAs, object hashes, workflow evidence, and remaining production boundary.

If any formal signing input, protected environment, publication destination, or real verification result is absent, the status remains `Candidate / No-Go`. Test certificates, synthetic data, skipped gates, partial package sets, or candidate-shaped fixtures cannot satisfy completion.

The public S06 update must include the applicable project memory files: `docs/project-memory/PROJECT_STATE.md`, `05-Completed.md`, `06-Todo.md`, and `CHANGELOG-AI.md`. Platform and CRM update their own P10 publication and consumer ledgers in their respective stages.

## 14. Implementation boundary

This document approves design and planning only. Implementation must use independent task branches/worktrees, test-driven contract changes, exact file-level staging, full diff review, and the repository's standard verification gates. It does not authorize production deployment, force-push, shared-history rewrites, remote-branch deletion, or weakening an existing R2 release gate.
