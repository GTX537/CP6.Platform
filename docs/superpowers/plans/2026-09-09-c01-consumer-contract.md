# C01 consumer contract implementation plan

Current result (2026-09-10): source delivery, immutable `0.10.2` publication, fixed consumption and actual C01 acceptance passed. The fifth real run completed 72/72 with zero failures/skips. [The original summary/JUnit and four failed attempts](https://github.com/GTX537/CP6.CRM/tree/main/docs/delivery/c01/real-identity-2026-09-10) bind Core `fb55a877de8ee8f9d27fd3bf8e73c824a21549e8`, actual CRM `37cf0e58ff146ed58582768cf2c91e3c9fbe81cf` and the published package hashes. CRM PR #58 integrates the identical tree at `c02055178d96acf4c15ece8c5ba0f51b01b86f2c`; source PR passed all seven applicable jobs, while six substantive main jobs passed and GitHub billing blocked the final aggregate job before startup. The historical overall main CI remains failed. The owner later explicitly authorized local verification instead of ordinary required Actions checks; CRM PR #59 and Core PR #100 normally delivered the original evidence. These Platform records now follow that policy. Execution and integration identities are never interchanged. The complete real rotation retained both publication and expiry/skew waits; owned SQL and processes were cleaned. C02 now proceeds under its approved design; the observations below are historical checkpoints.

The user's later verification-budget instruction supersedes the original default double-agent review sequence: reuse unchanged passing evidence, review one complete diff, use the owner-authorized local checks and confirm remote main without triggering Actions. Documentation does not trigger another local business suite.

> Execute with subagent-driven-development: one bounded implementation task at a time, followed by specification review and then quality review. Continue within the accepted C01 scope; do not claim completion before fixed-package and real cross-repository evidence.

Design: [C01 consumer contract](../specs/2026-09-09-c01-consumer-contract.md).

## Task 1: reproduce and close the Platform validation gaps

- [x] Create an isolated branch from verified Platform main `808a201f0cf6f877f8ca9c804e304585a29c446a` and run the existing authentication baseline: 23/23, zero skips.
- [x] Add focused failing tests for token type, literal kid matching, exact audience including trailing-slash negatives, and actual HTTP metadata cache boundaries. Keep the original baseline cases, with successful access-token fixtures explicitly typed at+jwt; retain separate JWT identity fixtures as rejection cases.
- [x] Add the bounded Platform metadata manager and register it through the actual bearer entry point. Preserve IdentityModel signature validation and P03's first-unknown-request failure behavior. Test concurrent fetch counts, cancellation, bounded streaming, invalid key/protocol invalidation and every cache deadline. Metadata failures must return actual HTTP 401 with the generic problem body, not 500.
- [x] Run the complete affected authentication suite and obtain independent specification and quality reviews; fix all findings.
- [x] Finish the seven repository gates and update local verification records.
- [x] Normally deliver the verified branch with all five required PR/main checks.

## Task 2: prepare and publish the immutable forward package

- [x] Read the current formal release contract and inspect every exact-version guard before preparing 0.10.2. Independent read-only review completed on 2026-09-09; findings and exact scope are recorded below. Retain read-only verification of historical versions and all signing, provenance, timestamp, package-set and feed checks.
- [x] Test the new exact-version registration and failure cases; deliver it through normal review and PR/main gates. Do not broaden a version allowlist into arbitrary stable versions.
- [x] Run the concrete existing formal publication path from a protected verified main source. Respect the protected Environment's owner action. Verify all seven packages on Windows and Linux and record exact byte-preserving feed read-back evidence.

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

Task 1 implementation commit `01182493551512b421681b42e5eb6c979db8147e` passed the final post-format complete ASP.NET Core suite: 223/223, zero failures or skips, recorded in `.artifacts/c01/final-verification/consumer-final-verification.trx` with process exit 0. Initial token-boundary RED had 25 passes and seven expected failures; further failing regressions covered metadata/cache headers, RSA integer validity, URI/backchannel registration and unknown-kid throttling versus mandatory ordinary refresh. In particular, three healthy-metadata/no-reusable-cache requests initially returned incorrect 401 responses; the fix confines unknown-kid throttling to extra refreshes of reusable fresh trust. At that initial implementation, full repository gates and independent reviews were still pending, along with immutable publication and real fixed-consumer acceptance.

The final reviewed implementation is `9b26875b744cd51715d33881870d4984f691b64f`. Specification and quality reviews both passed with no remaining findings. Follow-up regressions close completion-time trust/backoff boundaries, synchronize streamed-body timing after the client receives headers, and prevent framework postconfiguration from restoring a redirect/cookie-enabled default transport. Application-provided clients and handlers retain caller ownership; pre-existing registration order and multiple schemes are covered. The actual transport RED observed two expected failures; the corrected focused suite passed 8/8 and complete ASP.NET Core suite passed 231/231, zero failures or skips. Final full-suite evidence is `.artifacts/c01/full-aspnetcore-manager-guard-final/full-aspnetcore-manager-guard-final.trx`. Repository delivery, immutable publication and real fixed-consumer acceptance remain separate pending steps.

The final local run for that implementation passed Format, Build, Unit, Integration, E2E, Contract and Security. Unit passed 124/124, Integration 231/231, E2E 31/31, Architecture 98/98 and Release contracts 216/216, all with zero failures or skips. Contract's 20 checks include both PowerShell contract suites, package-content safety and equal hashes for both builds of the five runtime and five symbol packages. Security reported no known vulnerable packages across the 17 projects from the configured nuget.org audit source. All seven machine summaries and JUnit files report Passed under the reviewed implementation SHA; retained copies are in `.artifacts/c01/final-gate-evidence-9b26875`. Subsequent changes for branch submission only update these documentation records. Required remote checks remain pending until normal PR/main delivery.

Task 1 is now delivered by [PR #54](https://github.com/GTX537/CP6.Platform/pull/54), reviewed head `19c9d3af4cd6167870e13a610f79e740b3e9d8bb`, after [PR run 34404740669](https://github.com/GTX537/CP6.Platform/actions/runs/34404740669) passed all five required jobs. It merged normally to `main@31b92736b0f65afae4fa95020b6a11f258963b8d`; [exact-main run 34405918337](https://github.com/GTX537/CP6.Platform/actions/runs/34405918337) again passed Windows, Linux, real Dapr/Kafka, real SQL Server and P09 non-production runtime. The merge tree equals the reviewed head, remote main contains the task commits, and a fresh rebuild on the merged source passed all 231 ASP.NET Core tests without skips. Task 2 starts from this verified main in its own branch. These results complete source delivery, while formal publication and fixed-consumer acceptance remain pending.

## Task 2 registration review

The independent read-only audit identified this exact implementation scope:

- `p10-formal-packages.yml` and the four writer-chain entry points (`Pack-P10FormalPackages`, `New-P10FormalPackageSet`, `Test-P10FormalPrerequisites`, `Publish-P10FormalPackageSet`) accept only `0.10.2`. The two reader scripts (`Test-P10FormalPackageSet`, `New-P10FormalPublicationRecord`) retain exactly `.0`, `.1` and `.2`.
- The publication semantic validator and source/packaged formal-publication Schema add only `.2`. Root version, every package version and every feed identity stay bound. Add API/Schema and actual packed-Schema positive/negative matrices; updating only the source enum is insufficient.
- Update the repository audit version to `0.10.2.0`, Release's default development version to `0.10.2-test.local.1`, and the corresponding Foundation/Architecture assertions. Preserve the shared runtime developer baseline used by older gate scripts unless a separately justified dependency requires changing it; formal packing passes the exact release version explicitly.
- Close the existing record-generator normalization gap: it must validate each read-back package's own version and source before producing a publication record, just as it already validates each Windows/Linux package. A mismatched inner version/source must fail without generating a final record, even if the root and feed identity appear valid.
- Keep historical `.0` recovery, `.1` publication evidence, S02 test candidate identities, P10 candidates/Locators, trust policy/certificate bytes and existing package bytes unchanged. The old `.1` Release package cannot validate a new `.2` publication record; C01 needs a new fixed `.2` evidence consumer while retaining historical CRM proof projects.

Read-only external checks on 2026-09-09 confirmed strict Platform main protection with all five required validation contexts, administrator enforcement, force-push/deletion disabled, and the existing owner approval on `p10-formal-release`. All seven `0.10.2` version slots were absent at this preliminary check. The protected publication job must repeat the full fresh prerequisite gate; this observation neither consumes a version nor substitutes for approval or publication evidence.

Registration implementation `6ecc3269c88f9f1bf9c2569f453695e61afb3088`
passed all seven repository gates. Unit passed 124/124, Integration 231/231,
E2E 31/31, Architecture 98/98 and Release 235/235, with zero failures or skips.
The 20-check Contract gate includes both PowerShell suites and repeat-package
hash comparisons; Build completed without warnings, and Security reported no
known vulnerable packages across 17 projects from the configured audit source.
Machine summaries and zero-skip gate JUnit files bind that exact commit under
`.artifacts/c01/repository-gates-6ecc326`.

Focused registration tests first rejected the new valid version in the API,
source Schema and actual packed Schema. A separate inner read-back mutation
reproduced the record generator accepting an inconsistent package identity.
The isolated feed-newline case then exposed two further Schema acceptances;
the corrected matrix passed 41/41. Evidence is under
`.artifacts/c01/registration`. Independent specification review subsequently
found that the wrong-feed-package-ID tests also changed the version, masking
the intended boundary. Test-only commit
`06a327d63e4e992f7ef1dc70d8971efa29829b9f` derives each fixture's unchanged
version and changes only package ID. Removing only the ordinal package-ID
constraints produced two expected Schema failures and 39 passes; restoring the
original Schema bytes produced 41/41 passes. Format, specification re-review and
fresh independent quality review passed at `06a327d`. The final affected
Contract rerun passed all 20 checks at that exact commit: 235/235 Release and
98/98 Architecture tests, both PowerShell contract suites, package content
checks and equal hashes for both package builds, with zero failed or skipped.
The copied summary and JUnit are under
`.artifacts/c01/reviewed-contract-06a327d`. Normal source delivery and formal
publication subsequently completed as recorded below; final fixed-consumer
acceptance remains pending.

## Task 2 observed publication

Registration [PR #55](https://github.com/GTX537/CP6.Platform/pull/55), head
`0660001cb1357a5871d5f690e6a709163a66ccac`, passed all five required jobs in
[run 34415260032](https://github.com/GTX537/CP6.Platform/actions/runs/34415260032)
and merged normally to `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`. Its
[exact-main run 34416274332](https://github.com/GTX537/CP6.Platform/actions/runs/34416274332)
also passed all five jobs. Remote main contains the reviewed commits and its
tree matches the reviewed head; a fresh merged-source registration smoke passed
54/54 with zero failures or skips.

Owner `GTX537` approved the existing protected Environment for
[formal run 34417259187](https://github.com/GTX537/CP6.Platform/actions/runs/34417259187),
attempt 1, at that exact source. Both signing/publication/read-back on Windows
and independent verification on Linux passed. The seven `0.10.2` packages are
now immutable consumed versions. The final publication record hash is
`17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181`.
Both raw downloaded archives matched their GitHub digests before extraction;
their entry sets, all seven signed/read-back package hashes, the original
publication API and signed-package verifier were rechecked locally.
The [five original records and complete artifact/package identities](../../evidence/c01/0.10.2/README.md)
preserve actual publication evidence. Producer SDK `8.0.425`, NuGet `6.11.2.1`
and `windows-2025` are the recorded workflow toolchain, independently of local
consumer SDK selection. Trust remains pinned self-signed and does not claim
public CA trust. This record delivery still needs its own normal PR/main checks.

CRM cache source has separately completed PR #54 and verified main
`06d02b54c5e3df4da761f813a34cefb6ee1babd9`; its package-upgrade branch now starts
from that verified baseline. Actual fixed-feed consumption and the final real
issuer matrix remain pending, so C01 is not closed by package publication.
