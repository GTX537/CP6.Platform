# WP02-02 wave8 task-level QUALITY review

Reviewed 2026-10-15 in `codex/wp02-02-formal-registration-20261015`, against Platform base `66480fe24d51c706708be971e7e9d75a684a3bc4`. Review covers the current uncommitted bounded registration candidate, including new tests and private diagnostic tools. It is the single task-level quality review following the completed SPEC review; it does not reopen the corrected historical-reader finding.

**Implementation quality: no new actionable correctness, security, coverage, or maintainability finding identified within this bounded change. Delivery: BLOCKED; not merge-ready.** This conclusion does not waive failed gates or establish formal trust, publication, current Main adoption, or business acceptance.

## Review basis

The reviewer read the actual tracked diff and new registration tests/tools, rather than treating implementation or SPEC reports as proof. Existing supporting evidence was inspected without rerunning builds or tests. No source edits, packaging, signing, workflow execution, commit, push, or service startup occurred. The only file written by this review is this report.

Requirements were the wave8 `D:/CP6/docs/development/dispatch/20261015-wave8/WP02-02.md` scope and repository development rules. The previously completed 29-file immutable-input audit and its scope were retained; no second broad repository audit was initiated.

## Implementation assessment

| Area | Quality assessment |
| --- | --- |
| Formal API and schemas | Both validators enumerate only exact `.0/.1/.2/.3`. Existing semantic source/version/feed equality, distinct hashes, actual archive identities and evidence checks remain intact. Added schema branches bind each of seven ordered package/feed entries to its formal version. Common capability definitions retain exact `1.0.0`, `ctx-v1`, and `Background/Http/Message`; the new formal branch permits only `ctx-binding-v1`, while old versions retain empty capabilities and compatibility. Structural schema validation is not represented as cross-field or cryptographic verification. |
| Trust and consumer qualification | The change does not modify the formal signature-verifier boundary, independently approved source/policy/publication requirements, or exact measured-row matching. Unmatched requests remain Unknown. Expanded tests exercise all four formal versions without granting trust from shape alone and assert Unknown consumer/host/runtime/business states. Existing TaskLocal behavior remains separate from formal trust. |
| Writers, readers and workflow | Four writer parameter changes are exact `.3`; `New-P10FormalPublicationRecord` and `Test-P10FormalPackageSet` remain four-version readers. Workflow changes are limited to version description and the two exact version checks. No changed line weakens main/event/checkout binding, protected environments, pinned actions, signing, RFC3161/Current, one-build provenance, Windows/Linux validation, consumed-marker behavior or readback rules. |
| Regression coverage | New cases cover old/new versions, unknown/prerelease/metadata inputs, formal capability expansion, mixed package/feed identity, malformed source/hash, semantic source and duplicate-hash corruption, actual archive-byte corruption, static script parameter restrictions and actual packed schemas. Reader recovery is covered by the existing six-case post-correction regression. These tests address the changed behavior without adding a new production abstraction or broad refactor. |
| Actual package evidence | Independently read the recorded unsigned TaskLocal nupkg. Its SHA-256 matches `49ba29abe9eb050e54896229cec539144b188d4392d442fe34c6d30c3e97c24f`; `.signature.p7s` is absent. Both changed schema entries equal current source bytes. The unique prerelease and recorded dirty-source file hashes distinguish this diagnostic package from a publication of the base commit. |
| Private runner provenance | Compared every provenance-listed target with its actual Main `b3eb755bc43e370f697480a6ea41474c3d978488` Git object and checked source/target hashes; all recorded hashes matched. Differences are the specified Platform SHA/tree pin, task/owner paths, documentation and the owner-local import boundary with its regression. The probe, graph inspection, build capture and original trust behavior remain copied unchanged. |
| Private isolation and cleanup | The new owner boundary uses exclusive creation of inert `Directory.Packages.props` bytes and exact verification before resumed work. It blocks unwanted upward central-package discovery without changing nearer Platform/probe configuration. Existing owner-path, fresh-owner and reparse-point checks remain intact; source snapshots and original locks/configuration are retained. The private probe still binds a random loopback port and stops/disposes its own host. Actual attempt2 evidence records `ConnectionRefused`, successful disposal and restored propagators; no Main Program, database or broker execution is implied. |

The schema repetition is bounded to four explicit immutable versions and supports exact local validation without introducing dynamic schema machinery. No style-only restructuring is requested.

## Existing evidence and limits

The reviewer read TRX counters directly: targeted diagnostic **171/171 passed**, historical-reader regression **6/6 passed**, Architecture **98/98 passed**, and safe Release diagnostic **353 passed / 2 failed out of 355**. These are existing results, not new reviewer executions. The diagnostic Release commands retain NU1902/NU1903 as visible warnings and do not satisfy the failed standard command.

The private attempt2 state/graph/runtime evidence binds fixed Main `1129953156b7775152a74ad2f51cb95ef430ee56` and Platform `66480fe24d51c706708be971e7e9d75a684a3bc4`: three build roots succeeded, runtime evidence contains 31 assertions and 11 observed assemblies, and graph evidence records 15 assets, 15 deps, 17 locks and 189 packages with no graph errors. Runtime policy is only `MATCHED_SPECIFIED_PATHS`; formal trust remains BLOCKED and Main consumption NOT_RUN. The failed first NU1008 owner is preserved. The added import-boundary regression and existing 30-test tool result support the bounded environment correction; they are not producer-release acceptance.

Runtime evidence applies to the fixed diagnostic source closure. Changed Release validators/schemas are covered separately by API/schema/package evidence, not by the five-package private runtime probe. Final producer SHA remains null until COORD integrates and verifies remote main; this review does not relabel earlier execution as testing a future SHA.

## Known delivery blockers retained

1. The original standard Release command fails restore on immutable `System.Security.Cryptography.Pkcs 8.0.1` NU1902/NU1903. Command-level diagnostic overrides do not resolve that gate. The dependency question remains at Handoff `a602023b87148543ae512ef957bd284cc1c4a8c8`.
2. The existing out-of-window `P10PackageTests.cs` still rejects `.3` and omits the already present capability assets from its allowlist. The two actual failures remain visible; the scope question is at Handoff `14f86eb13a5061b822e625e52b2705f9d4d19c19`. They are not new review discoveries and are not waived.
3. Three signing-related tests remain explicitly excluded and unexecuted. Real author signing, RFC3161/Current, protected workflow execution, seven-package publication and feed readback are not validated by this review.
4. Formal trust, current Main business adoption, database/broker behavior and 17 PGT acceptance remain outside this bounded result. Pending DELIVERY completion is an outstanding delivery step, not an implementation defect.

No additional code change is requested by this quality review. The known gate and scope blockers still require coordination before integration; the candidate must not be described as merge-ready or formally published.
