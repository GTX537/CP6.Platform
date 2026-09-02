# P10 Storage Authority Contract Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `pinned-trust-store.v1` into exact alignment with the approved P10 R2 authority design before any formal package, cosign key, or public CP6 candidate is published.

**Architecture:** `CP6.Platform.Release` remains the sole contract owner. The v1 trust policy will carry one closed Cloudflare R2 authority containing the account-scoped endpoint inputs, jurisdiction, allowed prefixes, access mode, and object-size ceiling; the parser will retain and expose that already-validated mapping so S06 cannot accept storage coordinates from an unverified Locator. This is a pre-publication correction: no formal `CP6.Platform.Release` package exists yet, so no published contract is rewritten.

**Tech Stack:** .NET 8/C# 12, System.Text.Json, JSON Schema 2020-12, xUnit 2.9.3, PowerShell 7.

---

## Scope and fixed policy

The one accepted authority has these exact semantics:

```json
{
  "accessMode": "AuthenticatedReadConditionalCreate",
  "accountId": "11111111111111111111111111111111",
  "allowedPrefixes": ["candidates/platform/", "objects/sha256/"],
  "bucket": "cp6-release",
  "endpointTemplate": "https://{accountId}.r2.cloudflarestorage.com",
  "id": "cp6-release-r2-v1",
  "jurisdiction": "default",
  "maxObjectBytes": 4194304,
  "provider": "cloudflare-r2"
}
```

The all-`1` account ID above is the non-production fixture value. The later
reviewed public trust-policy instance supplies the real public account ID.

`AuthenticatedReadConditionalCreate` means consumers authenticate with a
bucket-scoped read-only credential. Publication uses a short-lived locally
signed R2 session credential and a single `PutObject` carrying
`If-None-Match: *`; normal publication has no list, delete, copy, or multipart
permission. The parent write identity is an Environment secret and is not part
of this repository change.

## File structure

```text
contracts/release/v1/pinned-trust-store.v1.schema.json
contracts/release/v1/fixtures/supporting/trust*.json
src/CP6.Platform.Release/Cp6PinnedTrustPolicy.cs
tests/CP6.Platform.ReleaseTests/TrustAndStorageValidationTests.cs
tests/CP6.Platform.ReleaseTests/SupportingContractValidationTests.cs
docs/P10-RELEASE-GOVERNANCE.md
CHANGELOG.md
```

## Task 1: Lock the missing authority behavior with failing tests

**Files:**

- Modify: `tests/CP6.Platform.ReleaseTests/TrustAndStorageValidationTests.cs`
- Create: `contracts/release/v1/fixtures/supporting/trust-authority.invalid.json`

- [x] **Step 1: Add the public mapping assertions**

Add a test that parses `trust.valid.json`, calls
`RequireStorageAuthority("cp6-release-r2-v1")`, and asserts every field below:

```csharp
Assert.Equal("cloudflare-r2", authority.Provider);
Assert.Equal("11111111111111111111111111111111", authority.AccountId);
Assert.Equal("default", authority.Jurisdiction);
Assert.Equal("https://{accountId}.r2.cloudflarestorage.com", authority.EndpointTemplate);
Assert.Equal("https://11111111111111111111111111111111.r2.cloudflarestorage.com", authority.Endpoint);
Assert.Equal("cp6-release", authority.Bucket);
Assert.Equal(["candidates/platform/", "objects/sha256/"], authority.AllowedPrefixes);
Assert.Equal("AuthenticatedReadConditionalCreate", authority.AccessMode);
Assert.Equal(4 * 1024 * 1024, authority.MaxObjectBytes);
```

Also assert that an unknown authority ID throws code `storage-authority`.

- [x] **Step 2: Add a semantic rejection assertion**

Create canonical `trust-authority.invalid.json` with the complete new authority
shape but `candidates/other/` in place of the approved Platform prefix. Assert
parsing throws `storage-authority`. This proves the policy cannot broaden its
trusted prefixes.

- [x] **Step 3: Run the focused tests and confirm RED**

Run:

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter FullyQualifiedName~TrustAndStorageValidationTests
```

Expected: compilation fails because `RequireStorageAuthority` and
`Cp6PinnedStorageAuthority` do not exist.

- [x] **Step 4: Commit the red test state only after recording the failure**

Do not commit a non-compiling intermediate. Continue directly to Task 2 while
preserving the RED output in the task record.

## Task 2: Expand the closed schema, fixtures, and parser

**Files:**

- Modify: `contracts/release/v1/pinned-trust-store.v1.schema.json`
- Modify: `contracts/release/v1/fixtures/supporting/trust.valid.json`
- Modify: `contracts/release/v1/fixtures/supporting/trust.revoked.valid.json`
- Modify: `contracts/release/v1/fixtures/supporting/trust-downgrade.invalid.json`
- Modify: `contracts/release/v1/fixtures/supporting/trust-missing.invalid.json`
- Modify: `contracts/release/v1/fixtures/supporting/trust-unknown.invalid.json`
- Modify: `contracts/release/v1/fixtures/supporting/trust-wrong-kind.invalid.json`
- Modify: `src/CP6.Platform.Release/Cp6PinnedTrustPolicy.cs`

- [x] **Step 1: Replace the three-field authority schema**

Require exactly these nine fields: `accessMode`, `accountId`,
`allowedPrefixes`, `bucket`, `endpointTemplate`, `id`, `jurisdiction`,
`maxObjectBytes`, and `provider`. Use these constraints:

```json
{
  "accessMode": { "const": "AuthenticatedReadConditionalCreate" },
  "accountId": { "type": "string", "pattern": "^[0-9a-f]{32}$" },
  "allowedPrefixes": {
    "type": "array",
    "minItems": 2,
    "maxItems": 2,
    "uniqueItems": true,
    "items": { "enum": ["candidates/platform/", "objects/sha256/"] }
  },
  "bucket": { "const": "cp6-release" },
  "endpointTemplate": { "const": "https://{accountId}.r2.cloudflarestorage.com" },
  "id": { "$ref": "https://schemas.cp6.dev/release/release-common.v1#/$defs/storageAuthority" },
  "jurisdiction": { "const": "default" },
  "maxObjectBytes": { "const": 4194304 },
  "provider": { "const": "cloudflare-r2" }
}
```

Keep `additionalProperties=false`.

- [x] **Step 2: Upgrade every trust fixture canonically**

Use the dummy account ID of 32 `1` characters in repository fixtures. Sort
authority properties and prefix values ordinally. Preserve the single intended
mutation in each existing invalid fixture. The new semantic invalid fixture
contains all valid fields and only replaces the approved Platform prefix with
`candidates/other/`.

- [x] **Step 3: Retain and expose the parsed mapping**

Change `Cp6PinnedTrustPolicy` to store:

```csharp
private readonly IReadOnlyDictionary<string, Cp6PinnedStorageAuthority> _storageAuthorities;
public IReadOnlyDictionary<string, Cp6PinnedStorageAuthority> StorageAuthorities => _storageAuthorities;
```

Add:

```csharp
public Cp6PinnedStorageAuthority RequireStorageAuthority(string authorityId)
{
    if (!_storageAuthorities.TryGetValue(authorityId, out var authority))
        throw Error("storage-authority", "Storage authority is not pinned.");
    return authority;
}
```

Replace `ValidateStorageAuthorities` with a parser that enforces the exact
constants above, validates the lowercase account ID, requires the two unique
ordinal prefixes, caps the object size at 4 MiB, rejects duplicate IDs, and
returns the dictionary. Pass that dictionary into the policy constructor.

Add the public immutable record:

```csharp
public sealed record Cp6PinnedStorageAuthority(
    string Id,
    string Provider,
    string AccountId,
    string Jurisdiction,
    string EndpointTemplate,
    string Bucket,
    IReadOnlyList<string> AllowedPrefixes,
    string AccessMode,
    long MaxObjectBytes)
{
    public string Endpoint => EndpointTemplate.Replace(
        "{accountId}", AccountId, StringComparison.Ordinal);
}
```

- [x] **Step 4: Run the focused tests and confirm GREEN**

Run the Task 1 command. Expected: all
`TrustAndStorageValidationTests` pass.

- [x] **Step 5: Run all Release contract tests**

```powershell
dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release
```

Expected: zero failures, including structural fixture and deterministic-byte
tests.

- [x] **Step 6: Commit the contract correction**

Stage only the schema, trust fixtures, parser, and tests. Review the cached
diff, then commit:

```text
fix(release): pin complete R2 storage authority
```

## Task 3: Document the corrected pre-publication boundary

**Files:**

- Modify: `docs/P10-RELEASE-GOVERNANCE.md`
- Modify: `CHANGELOG.md`
- Existing reference: `docs/superpowers/specs/2026-09-01-p10-release-governance-design.md`
- Existing plan: `docs/superpowers/plans/2026-09-02-p10-storage-authority-remediation.md`

- [x] **Step 1: Record the concrete authority contract**

Document the provider, authority ID, default jurisdiction, endpoint-template
derivation, bucket, two allowed prefixes, access mode, 4 MiB ceiling, permanent
read-only consumer boundary, and temporary conditional-create publisher
boundary. State explicitly that no credential, candidate, Locator, bundle, or
R2 object is created by this change.

- [x] **Step 2: Add a changelog entry**

Record that the pre-publication v1 trust contract now retains the complete
Cloudflare authority mapping required by the approved design and S06 verifier.

- [x] **Step 3: Run full proportional gates**

```powershell
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Build -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Unit -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Contract -Profile ci
pwsh -NoProfile -File ./eng/verify.ps1 -Gate Security -Profile ci
git diff --check
```

Expected: every gate exits zero. Docker-dependent probes may report the
repository's explicit `unavailable` result only where the existing contract
already permits it; no required P10 Release test may be skipped.

- [x] **Step 4: Review and commit documentation plus this plan**

Stage only the two documentation files and this plan, inspect the cached diff,
then commit:

```text
docs(release): record complete R2 authority contract
```

## Task 4: Review, merge, and verify exact Platform main

- [ ] **Step 1: Audit the branch**

Review `git diff origin/main...HEAD`, scan for secrets and machine-specific
paths, and verify the worktree is clean. The branch must not contain an actual
Cloudflare account ID, credential, cosign key, package publication, or workflow
dispatch.

- [ ] **Step 2: Push and create the focused PR**

Push `codex/p10-storage-authority-contract-remediation` and create a PR titled:

```text
Fix complete P10 R2 storage authority contract
```

- [ ] **Step 3: Wait for all Platform checks and merge only when green**

Do not bypass a failed check and do not dispatch a formal package workflow.
Use a normal merge commit and preserve the remote branch.

- [ ] **Step 4: Verify exact remote main**

Fetch `origin/main`, prove it contains both task commits, and require the
exact-main Build, Unit, Contract, Security, package-compatibility, and relevant
runtime checks to pass before returning to public CP6 S06 planning.

## Execution handoff

Inline execution in the current task was already selected. Use
`superpowers:executing-plans` sequentially and do not create subagents unless
the user explicitly changes that choice.

## Execution evidence

- RED: the focused test build failed because `RequireStorageAuthority` did not exist.
- GREEN: focused authority/supporting-contract tests passed 45/45; the complete Release suite passed 122/122 with zero skips.
- Full gates: Build, Unit (124/124), Contract (Architecture 98/98 and Release 122/122), and Security passed. The first Contract attempt encountered a transient external timestamp-script failure after all 122 Release tests passed; an isolated rerun exercised all seven synthetic signed packages successfully, and the subsequent complete Contract gate passed without product changes.
