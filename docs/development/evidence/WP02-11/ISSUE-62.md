# WP02-11 Issue 62 — observed rejection and incomplete response body

Issue: https://github.com/GTX537/CP6.Platform/issues/62 (P2, Open).

Independent integration review found that a received 401/403/409/422 could lose its non-retryable classification when body reading failed. `response` is assigned only after full buffering; an I/O exception or attempt timeout therefore admitted retries using the failure alone. The fix retains the observed status separately and prevents generic replay of a non-transient HTTP rejection. An uncertain verified write may still query its original result; a body failure does not create a business rejection or Applied result.

Branch `codex/bug-62-http-rejected-body-20261013` was created from freshly fetched and confirmed Platform main `817a2926b0799f70cc87f384967e0883c1943ec5`, then imported original WP02-11 candidate `0f7c69f13a26b36895cd40aca3fc17299e866b70`. This branch changes only issue 62. Issue 63 has its own branch; COORD's task candidate aggregates both after their focused verification. Original candidate branch is retained.

## Actual local RED/GREEN

- `implementation/coord-issue62-red.trx`: 24 discovered, 16 failed, 8 passed, zero skipped. All rejection combinations expected one send and observed three. The 200/503 body-failure controls passed.
- `implementation/coord-issue62-green.trx`: 24 discovered, 24 passed, zero failed/skipped. Read and fixture-only proven-write cases cover four rejection statuses crossed with body I/O and attempt-timeout failure; 200/503 controls retain their bounded retries. Assertions include send/query/authorization counts, typed state, original identity, outcome option and disposal.
- SDK `C:/Users/tt/.dotnet/dotnet.exe` 8.0.424, .NET/ASP.NET 8.0.30, Release. Both runs used `--no-restore --filter FullyQualifiedName~RejectedBodyRegressionTests` on `tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj`. Matching `.log` captures are retained.
- `coord-issue62-before-red-fingerprints.json` binds the RED source. `coord-issue62-before-green-fingerprints.json` binds corrected source and still contains the RED-build artifacts. `coord-issue62-after-green-fingerprints.json` binds corrected source/dependencies and GREEN artifacts. These are working-byte hashes; Git source commits are recorded by delivery separately.

The final combined HttpV2 suite will be run once after integrating both fixes. Unchanged historical legacy/architecture gates are not relabeled as executions of these binaries. No real Host/Owner adoption, package publication, database or deployment is included. The issue remains Open until coordinator verification of remote main containment; no push/PR/main merge is performed by this worker.
