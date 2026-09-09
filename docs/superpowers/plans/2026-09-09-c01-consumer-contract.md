# C01 consumer contract implementation plan

> Execute with subagent-driven-development: one bounded implementation task at a time, followed by specification review and then quality review. Continue within the accepted C01 scope; do not claim completion before fixed-package and real cross-repository evidence.

Design: [C01 consumer contract](../specs/2026-09-09-c01-consumer-contract.md).

## Task 1: reproduce and close the Platform validation gaps

- [x] Create an isolated branch from verified Platform main `808a201f0cf6f877f8ca9c804e304585a29c446a` and run the existing authentication baseline: 23/23, zero skips.
- [x] Add focused failing tests for token type, literal kid matching, exact audience including trailing-slash negatives, and actual HTTP metadata cache boundaries. Keep the original baseline cases, with successful access-token fixtures explicitly typed at+jwt; retain separate JWT identity fixtures as rejection cases.
- [x] Add the bounded Platform metadata manager and register it through the actual bearer entry point. Preserve IdentityModel signature validation and P03's first-unknown-request failure behavior. Test concurrent fetch counts, cancellation, bounded streaming, invalid key/protocol invalidation and every cache deadline. Metadata failures must return actual HTTP 401 with the generic problem body, not 500.
- [ ] Run all affected authentication/gateway tests and appropriate Platform gates. Obtain independent specification and quality reviews, fix findings, update project records and normally deliver the verified branch.

## Task 2: prepare and publish the immutable forward package

- [x] Read the current formal release contract and inspect every exact-version guard before preparing 0.10.2. Independent read-only review completed on 2026-09-09; findings and exact scope are recorded below. Retain read-only verification of historical versions and all signing, provenance, timestamp, package-set and feed checks.
- [ ] Test the new exact-version registration and failure cases; deliver it through normal review and PR/main gates. Do not broaden a version allowlist into arbitrary stable versions.
- [ ] Run the concrete existing formal publication path from a protected verified main source. Respect the protected Environment's owner action. Verify all seven packages on Windows and Linux and record exact byte-preserving feed read-back evidence.

## Task 3: CRM compatibility and fixed-package consumption

- [ ] Use the isolated CRM branch from main `7651f4a1c65cae8604a8347ae6b872e6819ba800`. Prove the current client behavior against the actual producer headers before fixing any directive mismatch.
- [ ] Keep UUID user/sid, nonce, audience and CRM scope checks. Add focused cache-header/unknown-kid concurrency regressions and preserve real browser login/logout coverage.
- [ ] Replace actual application package references with the new exact immutable version, restore from the authoritative feed, and verify package identity. Never substitute ProjectReference or a local unsigned feed for final consumer evidence.
- [ ] Run appropriate CRM gates, independently review, normally merge and verify exact main.

## Task 4: real issuer-to-consumer acceptance and C01 closure

- [ ] Bind verified CP6 producer main, verified CRM main and published Platform package identities. Require actual SQL, issuer HTTPS/certificate and external private fixture inputs.
- [ ] Implement one reproducible HTTP acceptance entry point with real issuer tokens and actual consumers, including negative transport injection and measured cache refresh counts. Use CRM AuthorizationUriAsync for cache-only observations and distinguish unmodified producer's 60-second closure from a labeled policy case that proves success just below and failure at/after the independent 900-second cap. Produce zero-skip JUnit and a content-free summary even on failure.
- [ ] Run the complete accepted matrix, inspect the public evidence for credentials/PII, independently review the runner and normally deliver its source/evidence.
- [ ] Update all four CP6 project records and corresponding consumer records only after remote main and final fixed-package checks pass. Then advance C02/C03; do not convert C04 prerequisites or production approval into implementation assumptions.

Preparatory baseline evidence is under `artifacts/c01/baseline` and is not cross-repository acceptance. The first default PATH invocation found only SDK 10; the recorded passing baseline used the already-installed SDK 8.0.424 without changing global.json or installing a new toolchain.

Task 1 implementation commit `01182493551512b421681b42e5eb6c979db8147e` passed the final post-format complete ASP.NET Core suite: 223/223, zero failures or skips, recorded in `.artifacts/c01/final-verification/consumer-final-verification.trx` with process exit 0. Initial token-boundary RED had 25 passes and seven expected failures; further failing regressions covered metadata/cache headers, RSA integer validity, URI/backchannel registration and unknown-kid throttling versus mandatory ordinary refresh. In particular, three healthy-metadata/no-reusable-cache requests initially returned incorrect 401 responses; the fix confines unknown-kid throttling to extra refreshes of reusable fresh trust. Full repository gates and independent reviews remain pending, as do immutable publication and real fixed-consumer acceptance.

## Task 2 registration review

The independent read-only audit identified this exact implementation scope:

- `p10-formal-packages.yml` and the four writer-chain entry points (`Pack-P10FormalPackages`, `New-P10FormalPackageSet`, `Test-P10FormalPrerequisites`, `Publish-P10FormalPackageSet`) accept only `0.10.2`. The two reader scripts (`Test-P10FormalPackageSet`, `New-P10FormalPublicationRecord`) retain exactly `.0`, `.1` and `.2`.
- The publication semantic validator and source/packaged formal-publication Schema add only `.2`. Root version, every package version and every feed identity stay bound. Add API/Schema and actual packed-Schema positive/negative matrices; updating only the source enum is insufficient.
- Update the repository audit version to `0.10.2.0`, Release's default development version to `0.10.2-test.local.1`, and the corresponding Foundation/Architecture assertions. Preserve the shared runtime developer baseline used by older gate scripts unless a separately justified dependency requires changing it; formal packing passes the exact release version explicitly.
- Close the existing record-generator normalization gap: it must validate each read-back package's own version and source before producing a publication record, just as it already validates each Windows/Linux package. A mismatched inner version/source must fail without generating a final record, even if the root and feed identity appear valid.
- Keep historical `.0` recovery, `.1` publication evidence, S02 test candidate identities, P10 candidates/Locators, trust policy/certificate bytes and existing package bytes unchanged. The old `.1` Release package cannot validate a new `.2` publication record; C01 needs a new fixed `.2` evidence consumer while retaining historical CRM proof projects.

Read-only external checks on 2026-09-09 confirmed strict Platform main protection with all five required validation contexts, administrator enforcement, force-push/deletion disabled, and the existing owner approval on `p10-formal-release`. All seven `0.10.2` version slots were absent at this preliminary check. The protected publication job must repeat the full fresh prerequisite gate; this observation neither consumes a version nor substitutes for approval or publication evidence.
