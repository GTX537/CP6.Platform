# WP02-25 issues #68 / #69 correction batch

**LOCAL_PASS / FOCUSED_REVIEW_PENDING.** Original candidate `095a66bfda8b4df2ee65e920541ce6304e8e66c1`; aggregate tested source `ae4b56abf88aa40f1ba3bf82f1ef4c83a878b378`. This report does not close either Issue or claim remote-main integration.

Both issue worktrees were created from freshly fetched and remotely confirmed `origin/main=e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`. Each explicitly merged the original candidate as prerequisite before editing. The original worker remains clean at its original SHA. The aggregate branch preserves both independent fix histories without squashing or rewriting.

| Issue | Fix commit | Branch tip including console evidence |
| --- | --- | --- |
| [#68](https://github.com/GTX537/CP6.Platform/issues/68) | `db728d17d95147be89ab4afd7bbbab9ebe15cbcb` | `b6d65189bc22569226667e19d85a4f52e5c9f4a5` |
| [#69](https://github.com/GTX537/CP6.Platform/issues/69) | `621f0abab99db5708ccbe46fc592f33333ca08b1` | `dba10010687d19de9fe2b07c182e2af54ea51d83` |

Issue branches: `codex/bug-68-identity-freshness-20261014`, `codex/bug-69-operational-no-store-20261014`. Aggregate: `codex/wp02-25-corrected-20261014` in `D:/CP6.Platform-worktrees/wp02-25-corrected-20261014`.

## Changes

- #68 captures the original monotonic timestamp from the observer-owned clock before byte loading. Evaluation checks this elapsed budget and the existing UTC metadata. Expiry latches on the immutable-manifest evaluator; later UTC correction cannot revive it. A separate evaluator clock cannot reset the observer's age. Clock failure remains safe Unproven. The public shape and scoped/nonproduction meaning are unchanged.
- #69 adds Cache-Control:no-store to live and identity, matching startup/ready. Four real handler responses and both unhealthy dependency responses are covered. Existing authorization and JSON/status behavior remain unchanged; authorization middleware denial output is outside this handler-only correction.

## Actual local evidence

All correction tests ran with **SDK 8.0.424 / .NET 8.0.30**. This supersedes the review-only PowerShell .NET10 reproduction for the two defects.

| Run | Result | Exit | Evidence |
| --- | --- | --- | --- |
| #68 RED | 7 failures / 7, expected stale Match and rollback HTTP 200 | 1 | [TRX](../issue-68/red-01/results.trx), [console](../issue-68/red-01/console.log) |
| #68 GREEN | 7/7 passed | 0 | [TRX](../issue-68/green-01/results.trx) |
| #69 RED | 2 failed / 6, live/identity missing header; 4 controls passed | 1 | [TRX](../issue-69/red-01/results.trx), [console](../issue-69/red-01/console.log) |
| #69 GREEN | 6/6 passed | 0 | [TRX](../issue-69/green-01/results.trx) |
| Aggregated affected regression | **69/69 passed, zero failures/skips** | 0 | [TRX](focused-01/results.trx), [execution](focused-01/execution.json), [console](focused-01/console.log) |

The combined filter covers new issue regressions, runtime identity, negative identity matrix, health registration/failures/permission, route/module isolation, monotonic dependency cache and legacy operational endpoints. It was executed once after both fixes were merged; no full repository test rerun. It contains 56 earlier host cases and 13 new cases.

Actual Kestrel evidence includes fresh ready=200, expired=503 and UTC-rollback=503 while independent read remains available, identity/effect stay Unproven and live stays 200. The no-store test records all four real endpoint responses plus unhealthy startup/ready. No real system clock, database or Owner state was changed.

[verification.json](verification.json) verifies all **263 source/config hashes, 7 resolved dependency files and 25 artifact hashes** against current working bytes, with unchanged before/after inputs. Source digest `7210d14fc5eeefeb5c7a2200db5ee41e74697f112589f93bf7b2e10917fab45a`; focused TRX SHA-256 `da7c92ed8a72f87fc5f245da306c0be3398282e1ca22ed89a47ba2507bb3d0d5`. Exact command/SDK/time/TRX and binary hashes are retained per run. [Reusable local runner](../run_gate.py) reproduces the capture with a new stage name.

## Reuse and review boundary

The old 255/255 remains the original candidate's recorded execution, not a newly executed corrected-candidate gate. Unchanged Unit 57, Architecture 98 and 44 untouched host cases retain that bounded prior evidence. Their new binary metadata is not asserted byte-identical. No public/dependency/project/schema/architecture boundary changed; current affected behavior is established by the new 69-case run. Both issue GREEN runs are also retained without redundant reruns after commits.

Self-review covered the complete correction diff and evidence: exactly two production files, two task-private test files, the consumer note and unique correction evidence. `git diff --check` passes. No legacy evaluator/recorder/EF/Messaging/Owner/source definition was changed. Fixed source original 32 business AC remain NOT_RUN; trusted package, production SLO and actual Main/CRM adoption are still unproven. Independent focused review is intentionally pending COORD.

No push, PR, Issue update/closure, Actions trigger/cancellation/configuration, shared state write, database, package or environment operation. COORD owns workflow prepush audit, integration, state synchronization, remote-main verification and eventual Issue closure.
