# WP02-11 Issue 63 — authority faults preserve the original outcome

Issue: https://github.com/GTX537/CP6.Platform/issues/63 (P2, Open).

Independent integration review found that ordinary exceptions from `VerifyBindingAsync` and `AuthorizeReplayAsync` escaped the handler without a typed outcome. The latter could erase the observable Unknown state after a sent write and original-result query. The new shared local authority boundary catches synchronous and faulted-task exceptions, checks actual caller/parent cancellation and total deadline first, and returns the existing safe `RecoveryUnavailable` failure with original identity/state/attempt count. An unidentified Polly timeout remains `UnknownTimeoutSource`. Ordinary cancellation still reaches the existing source-aware cancellation logic. No new public type, enum or permissive retry behavior was introduced.

Branch `codex/bug-63-http-authority-outcome-20261013` was created independently from confirmed Platform main `817a2926b0799f70cc87f384967e0883c1943ec5`, then imported original WP02-11 candidate `0f7c69f13a26b36895cd40aca3fc17299e866b70`. Issue 62's fix is not part of this branch. COORD's final task candidate aggregates the two independent issue branches; original candidate and issue branches remain available.

## Actual local RED/GREEN

- `implementation/coord-issue63-red.trx`: 24 discovered, 22 failed, 2 passed, zero skipped. Ordinary transport, I/O, unknown-timeout and other faults escaped as raw exceptions at both authority stages, including concurrent caller/parent cancellation and total expiry. Two unrelated-cancellation controls passed.
- `implementation/coord-issue63-green.trx`: 24 discovered, 24 passed, zero failed/skipped. Assertions cover synchronous and faulted-ValueTask faults; safe top-level message, RequestOption outcome, original identity, NotSent versus Unknown, attempts, no extra query/replay and response disposal; cancellation and real total expiry retain precedence.
- SDK `C:/Users/tt/.dotnet/dotnet.exe` 8.0.424, .NET/ASP.NET 8.0.30, Release. Both runs used `--no-restore --filter FullyQualifiedName~AuthorityFaultRegressionTests` on `tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj`. Matching `.log` captures are retained.
- `coord-issue63-before-red-fingerprints.json` binds RED source. `coord-issue63-before-green-fingerprints.json` binds corrected source and RED-build artifacts. `coord-issue63-after-green-fingerprints.json` binds corrected source/dependencies and GREEN artifacts. Hashes are exact working bytes, with final source Git pin provided separately.

The combined HttpV2 gate is deferred to the final local task integration and will run once there. Historical legacy/architecture results retain their original provenance. No production authority or Host is installed; exceptions are not permission or durable Owner evidence. No Actions, package, database or deployment operation occurred. The issue remains Open pending coordinator verification of remote main containment.
