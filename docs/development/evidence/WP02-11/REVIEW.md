# WP02-11 independent task review

## Specification review — initial FAIL, subsequently corrected

Reviewer: fresh `/root/wp0211_spec_review`; entire task source/tests, fixed original requirements, evidence and allowed window inspected read-only. No test suite was rerun by the reviewer. Reviewed bytes correspond to `implementation/after-final-fingerprints.json` (235 source + 8 dependency + 25 compiled artifact entries).

Two material P1 findings in `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Handler.cs`:

1. Response-body `IOException`/`HttpIOException` bypasses the `HttpRequestException` handling path. A verified sent write could escape as a raw I/O exception without original-result query or its typed Unknown outcome. Relevant pre-fix locations: lines 161–164 and 282–284. Required regression: accepted fixture binding, response stream throwing I/O, query attempt and preserved original identity/state; real truncated-body transport where practical.
2. Completed authority tasks can bypass cancellation/deadline checks. `WaitAsync` can return an already-completed task after cancellation. Original-result query lacked post-completion query/total checks; a callback consuming the remaining budget could still return Applied or allow the old 503 response to escape. Verify/replay callbacks cancelling the caller and returning false could produce BindingRejected/ReplayDenied instead of cancellation. Relevant pre-fix locations: lines 88–89, 184–201 and 249–262. Required checks: caller/parent cancellation and relevant budget immediately after callbacks/body completion and before result interpretation, plus late-response disposal.

The reviewer confirmed initial transport/body completion checks and frozen `canRetry || canQuery` clones for verified Disabled/retry-zero calls already worked. No additional finding for those paths. Custom primary transport and cross-call recovery remain explicit Host boundaries.

The reviewer independently confirmed all 268 current fingerprints, original 128/98/98 passing gates, preserved parser RED, and exact 42 original AC rows with business NOT_RUN. These passing tests did not invalidate the two uncovered defects.

Corrections were assigned as one batch to the implementation worker. Original TRX/logs/manifests remain historical evidence; new regression/final affected-suite results will have distinct names. Unaffected old HTTP/gateway/auth and Architecture results may be reused only with their exact former source context and unchanged behavior/configuration rationale.

## Targeted specification recheck

First recheck: original I/O recovery and completed-callback overruns resolved. The reviewer independently read `review-fixes-red.trx` (16 executed, 11 failed, 5 passed) and `review-fixes-http-v2.trx` (146/146 passed, zero skipped), and verified 236 source + 8 dependency + 25 artifact hashes. Source changes from the original final manifest were limited to the handler, loopback tests and new `ReviewRegressionsTests.cs`; old implementation/configuration inputs stayed unchanged.

Finding 2 retains one P1 cancellation-source boundary: `Cp6HttpDeadline` combined caller/timer/parent cancellation without retaining caller identity, while the handler inspected only its immediate `SendAsync` token. A parent created with a caller token, passed to a child using `CancellationToken.None`, could map inherited caller cancellation to TotalTimeout instead of OCE/Cancelled, including cancellation before entry. The existing timeout must continue to mean a real timer deadline. This remaining boundary was sent for targeted correction; no new full-task review was started.

Final targeted verdict: **PASS**. The reviewer verified recursive preservation of the original caller token, entry checks for an already-cancelled parent, and cancellation propagation through transport/wait/query. Actual timer expiry continues to map to TotalTimeout. Targeted inherited-cancellation RED is 3 failed / 1 passed; current `parent-cancellation-http-v2.trx` is 150/150 passed, zero skipped. All 236 source + 8 dependency + 25 artifact hashes match; pre-GREEN and post-GREEN source/dependency arrays agree. The reviewer performed no test execution and did not relabel reused legacy/architecture results.

## Independent code quality review

**PASS**, fresh `/root/wp0211_quality_review` after specification recheck passed. Read all 10 new production files, all 10 new test files, additive legacy overloads and API/task documentation. Checked immutable requests and authority boundaries, Unknown query ordering, cancellation/timeout races, bounded retries, resource disposal and safe error output. No Critical/Important issue or in-scope Minor change remained.

The reviewer verified the staged write window, the 18-line additive old-file change, no unstaged source difference, clean staged diff check, current 150/150 zero-skipped result and manifest SHA-256 `5147dd29f33bfbcc8f82e4de9c32b381eb3e700a7066fa4f8d8ef82f64565341`. It did not run tests/builds, expand legacy/architecture evidence scope or claim real Host/Owner adoption or remote main delivery.
