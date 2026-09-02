# P10 Cosign Trust Binding Implementation Plan

> **Status:** implementation plan for a narrow pre-bootstrap contract repair

**Goal:** Make every `pinned-trust-store.v1` key cryptographically bind its
declared `keyId` to one canonical cosign-compatible public key before any real
P10 key is generated or trusted.

**Scope:** This task changes only the Platform-owned trust contract, fixtures,
tests, and release-governance documentation. It does not generate a production
key, create a GitHub Environment or Secret, publish a package, write R2, or
dispatch a workflow.

**Decision:** `publicKey` is the canonical PKIX `PUBLIC KEY` PEM for an ECDSA
P-256 key, encoded with LF separators and no trailing newline. `keyId` remains
`sha256:<lowercase SHA-256 of DER SubjectPublicKeyInfo>`. This is the direct
output shape used by cosign after line-ending normalization and gives the
validator deterministic bytes to pin.

## Task 1: Add failing semantic contract coverage

**Files:**

- Modify: `tests/CP6.Platform.ReleaseTests/TrustAndStorageValidationTests.cs`
- Modify: `contracts/release/v1/fixtures/supporting/trust.valid.json`
- Modify: other supporting trust fixtures that must remain semantically valid

**Steps:**

1. Replace placeholder trust-key strings with two deterministic test-only
   ECDSA P-256 PKIX public keys and their exact SPKI SHA-256 key IDs.
2. Add negative tests for malformed PEM, non-canonical PEM, and a declared
   key ID that differs from the parsed SPKI digest.
3. Run the focused Release test project and retain the expected RED result
   showing the current parser accepts at least one invalid binding.

## Task 2: Enforce canonical PEM and SPKI identity

**Files:**

- Modify: `src/CP6.Platform.Release/Cp6PinnedTrustPolicy.cs`
- Modify: `contracts/release/v1/pinned-trust-store.v1.schema.json`

**Steps:**

1. Parse `publicKey` as a PKIX PEM public key using .NET cryptography APIs.
2. Require ECDSA P-256, export DER SubjectPublicKeyInfo, and reconstruct the
   canonical PEM form.
3. Reject malformed, unsupported, or non-canonical key material with the
   existing `trust-key` failure code.
4. Hash the DER SPKI and require exact equality with `keyId`.
5. Document the semantic format in the JSON Schema without treating schema
   validation as a substitute for the cryptographic parser.

## Task 3: Close documentation and regression gates

**Files:**

- Modify: `docs/P10-RELEASE-GOVERNANCE.md`
- Modify: `CHANGELOG.md`

**Steps:**

1. Record the canonical key encoding and key-ID derivation.
2. State that real trust bootstrap remains blocked until this change is merged
   and exact-main verification succeeds.
3. Run:

   ```powershell
   dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release
   dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release
   pwsh -NoProfile -File eng/verify.ps1 -Gate Contract -Profile ci
   pwsh -NoProfile -File eng/verify.ps1 -Gate Security -Profile ci
   ```

4. Review the complete diff against `origin/main`, scan for private material,
   commit only the listed task files, push a PR, and require the full remote
   matrix plus exact-main verification before trust bootstrap resumes.
