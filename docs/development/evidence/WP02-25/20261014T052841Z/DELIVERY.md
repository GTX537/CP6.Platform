# WP02-25 candidate delivery — runtime identity, health and telemetry V1

Status: LOCAL_GATE_PASS / READY_FOR_COORD_REVIEW. This report does not mark the task DONE. COORD owns serial integration, authoritative roadmap/four-state-file synchronization and remote-main verification. The candidate commit and remote publication identity will be supplied by the fixed Handoff DELIVERY message, outside this commit.

## Source and implementation

- Assignment: Main `1ab2d031e3c501662ecb89a33839fac1cba55d33`, Handoff `ad255c21810a101662fd3e380992a03fde71407e`; actual START `e1b65c2cfc9cb243914687c1d2af82fd82ffc233`.
- Platform baseline, test-time HEAD, local main and freshly fetched origin/main: `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`. Branch: `codex/wp02-25-observability-20261013`.
- Tests ran on the baseline plus uncommitted task bytes, not a pre-existing committed candidate. Final source/config snapshot digest: `a68ad22bcaddba0deec96403fe683de35d73a81bf3e4cb037a258aaa2393284a`. `snapshot-verification-final-01.json` contains the 261 individual source/config hashes, resolved dependency hashes and 33 local Release artifact hashes. Gate-specific before/after snapshots record each actual input set; these are local build artifacts, not published packages.
- Added ten production files in the two `ObservabilityV1` namespaces and eleven task-private test files. No existing production/test file, project, dependency, version, schema, workflow or shared state file changed. Public contract namespace version is 1.0.0; no package version/publication is implied.
- Explicit opt-in registration exposes actual owned managed-module byte observation, separately pinned identity comparison, required health inventories and per-route dependency/module availability. Conservative aggregate failure and healthy independent reads coexist. Availability never grants business write permission.
- Actual OpenTelemetry trace, metric and log paths project to closed finite keys/values before their queue/aggregation boundary. Queues, rate, series, record size/count, retention, export concurrency, flush and disposal are bounded. Export/telemetry errors stay out of request results. Only local sinks are enabled.
- Metric definitions preserve existing transport/pre-commit semantics. Separate Owner-receipt latency contracts require explicit clock provenance and report unavailable measurements as not measured. No real Owner receipt feed was implemented.
- Consumer registration, lifetime and trust boundaries: [WP02-25-OBSERVABILITY-V1.md](../../../../WP02-25-OBSERVABILITY-V1.md).

## Formal local verification

Windows 11, SDK 8.0.424 / runtime 8.0.30 using `C:/Users/tt/.dotnet/dotnet.exe`; exact SDK output, command arrays, UTC timestamps and actual independent process exit codes are retained per suite.

| Gate | Passed / executed | Skipped | Exit | Evidence |
| --- | ---: | ---: | ---: | --- |
| Unit: new V1 plus legacy telemetry conventions, release identity and SLO contracts | 57 / 57 | 0 | 0 | [execution](final-01/unit/execution.json), [TRX](final-01/unit/wp02-25-unit.trx), [console](final-01/unit/console.log) |
| ASP.NET Core: new V1 plus legacy endpoints, registration, end-to-end and propagator | 100 / 100 | 0 | 0 | [execution](final-01/host/execution.json), [TRX](final-01/host/wp02-25-host.trx), [console](final-01/host/console.log) |
| Architecture: full existing project | 98 / 98 | 0 | 0 | [execution](final-01/architecture/execution.json), [TRX](final-01/architecture/wp02-25-architecture.trx), [console](final-01/architecture/console.log) |

Total 255/255, zero failures and skips. The gate filters are exactly those in the execution package. `final-verification-final-01.json` verifies each actual exit, TRX, new V1 case presence, before/after/current relevant input sets and dependency hashes. `eng/wp02-25/run_gate.py`, `verify_final.py`, `build_bindings.py` and `map_acceptance.py` provide reproducible task-private entry points. No business test is rerun merely because a commit SHA changes; equivalence is limited to recorded unchanged inputs.

The final host TRX retains actual observations:

- `OwnedBytesMatchOnlyExactPinnedInventoryAndFreshProcessBinding`: actual loaded owned-byte SHA-256 `0d068929cfcc3613c534a7be4ad1fdaac580a6d61eade4c17aa484a4eb5010ef`, compared with the local fixture's separately supplied pin. Scope is only owned managed modules.
- `RealKestrelDependencyStopRestartAffectsReadinessButNotLiveAndNeverGrantsNewEffects`: actual host `127.0.0.1:57683`, owned dependency `127.0.0.1:57682`, dependency stop/restart on the same port, constant live response, restored read availability and host disposal. Aggregate 503 is intentional because the fixture's effect identity remains unproven.
- `ActualSdkFinalTraceMetricAndLogExportsContainOnlyClosedSafeFields`: actual Kestrel HTTP 200 at `127.0.0.1:57700`, final SDK sink output for automatic inbound/outbound traces, aggregated metrics and logs. Adversarial tests cover seeded secret/case/resource/cardinality fields, blocking/throwing sinks, drop bounds, registration drift and lifecycle cleanup.
- At closeout no testhost/dotnet process command line belonging to this worktree remained. No unrelated process was stopped.

## Review and failure preservation

One whole-task specification review and quality review were completed, with targeted correction reviews only. [Specification correction review](reviews/spec-02-corrections.md) is PASS; [quality correction review](reviews/quality-02-corrections.md) is APPROVED.

Specification findings corrected: route-specific dependencies and module results; exact canonical string anchors rejecting terminal control characters; UTC/epoch/future validation of actual observation time. Quality findings corrected: null/oversized metric keys cannot throw through Counter.Add; logging/disposal races cannot throw back to callers; monotonic cache expiry cannot be extended by UTC rollback. Runs 23–27 preserve the failing regressions and targeted passing checks. Final formal gates above include these corrections.

All implementation failures remain under `implementation/`; [execution ledger](implementation/EXECUTION-LEDGER.md) and `trx-summary-final.json` distinguish compiler failures, assertion failures and later passing runs. For runs 01–17 the immediate numeric dotnet exit was not independently captured; it is explicitly unknown. Intermediate runs do not carry complete before/after source hashes and cannot independently prove final candidate provenance. Runs 18 onward retain immediate exit codes; the three final parent gates establish final source/dependency/artifact provenance. Reviewer approvals are reviews of code/evidence, not claims that reviewers executed tests.

## Original AC and remaining ownership

`acceptance-final.json` preserves all 32 exact original PLT-OBS-01 case identities, assertions and source lines. Original source hash: `20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99`. Twenty-seven cases have explicitly bounded local/regression portions mapped to exact passed final TRX names and hashes; five have no local execution. **All 32 original business acceptance results remain NOT_RUN.** Local portions never promote the whole case or target.

Unproven/not executed: whole running host/default-context/package/container identity; trusted published package provenance; actual Main/CRM adoption; PostgreSQL dependency/transaction behavior; actual Owner receipts, business population or latency; external exporters and operational retention/permissions; fleet rollout/rollback; independent SLO/production qualification. WP02-26, WP02-28 and WP02-29 retain their assigned responsibilities. Other logging/providers are outside this adapter's redaction guarantee.

No database created or queried; no migration, environment deployment, tag, package publication, Actions invocation/rerun/cancellation or trigger/settings change. Fresh queued/in-progress Platform run queries were empty. Candidate/protected-main event/filter/dependency inspection is retained in `workflows-prepush.json`; only manual ordinary workflows and the existing nonmatching RFC3161 narrow PR trigger are present. No task touches its two trigger paths.

COORD next action: review the fixed candidate full diff and these evidence boundaries, perform normal serial integration and necessary integration smoke checks, synchronize authoritative state, and verify remote main contains the candidate before declaring DONE. The worker retains its branch/worktree until that closeout.
