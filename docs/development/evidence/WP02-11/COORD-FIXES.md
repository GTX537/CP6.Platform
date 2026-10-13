# WP02-11 coordinated integration corrections

This record supersedes the original candidate's 150-case current-gate statement for the corrected local candidate. The original candidate and all its source, failure and test evidence remain preserved. Remote delivery and Main/CRM/Owner adoption are still separate coordinator gates.

## Identity and issue branches

- Original reviewed candidate: `0f7c69f13a26b36895cd40aca3fc17299e866b70`, from original baseline `9259a45d39bae7e98ca804a74778a588c1ad4619`.
- Current integration baseline: confirmed/fetched Platform main `817a2926b0799f70cc87f384967e0883c1943ec5`, including independently delivered WP02-07. Its unrelated changes are inherited, not attributed to this HTTP correction.
- Issue [62](https://github.com/GTX537/CP6.Platform/issues/62): branch `codex/bug-62-http-rejected-body-20261013`, fix `a09762b` plus separately retained console evidence `a52f416`. Only this issue's implementation/regression is changed on that branch.
- Issue [63](https://github.com/GTX537/CP6.Platform/issues/63): independent branch `codex/bug-63-http-authority-outcome-20261013`, fix `8395e75`. It does not contain issue 62's fix.
- Both issue branches were created from the current confirmed main above and imported the original candidate. Both were normally merged into local `codex/wp02-11-coord-fix-20261013`. The combined tested source commit is `58e35c36e1987105c144a6dcc1955a524db8ca35`; later evidence-only commits do not change tested source. The coordinator receives the final full candidate SHA separately to avoid self-referential evidence.

## Behavior corrected

1. The handler now retains observed status independently from response-body completion. A known non-transient rejection cannot become replayable because its body is truncated or times out. Original-result recovery remains available to a verified uncertain write, but no new-effect authorization or resend follows the known rejection. Controls confirm that 200-body loss and transient 503 still receive their bounded eligible recovery.
2. Both binding verification and replay authorization pass through one local exception boundary. Synchronous/faulted-task failures produce a safe typed outcome, with original identity, attempts and NotSent/Unknown preserved. Ordinary authority failure maps to existing `RecoveryUnavailable`; unidentified Polly timeout maps to existing `UnknownTimeoutSource`. Caller/ancestor cancellation or true total expiry is checked before interpreting an adapter fault. No new public API, failure enum or authority grant was added.

The only production correction is `HttpV2/Cp6HttpV2Handler.cs`. New private tests are `RejectedBodyRegressionTests.cs` and `AuthorityFaultRegressionTests.cs`. There is no change to legacy HTTP, authentication, gateway, ProblemDetails behavior, shared contracts, project/dependency settings or production registrations.

## Actual verification and exact bytes

| Retained execution | Discovered | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| coord-issue62-red.trx | 24 | 8 | 16 | 0 |
| coord-issue62-green.trx | 24 | 24 | 0 | 0 |
| coord-issue63-red.trx | 24 | 2 | 22 | 0 |
| coord-issue63-green.trx | 24 | 24 | 0 | 0 |
| coord-final-http-v2.trx | 198 | 198 | 0 | 0 |

RED was observed before each production fix and failed for the reported behavior. GREEN is the corresponding narrow regression; the 198-case combined HttpV2 suite ran once after normal local integration. No required failure was skipped or relabeled. The new cases include original identity and safe outcome preservation, disposal, no extra send/query/authorization, real caller and inherited caller cancellation, total expiry, and unrelated cancellation controls. They test the actual handler with deterministic private transport/authority fixtures; they do not claim durable production Owner proof.

All five runs use existing `C:/Users/tt/.dotnet/dotnet.exe`, SDK 8.0.424 / .NET and ASP.NET runtime 8.0.30, Release, Windows x64, `--no-restore`. The project is `tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj`. Filters are `FullyQualifiedName~RejectedBodyRegressionTests`, `FullyQualifiedName~AuthorityFaultRegressionTests`, then `FullyQualifiedName~HttpV2`. Each uses `--logger 'trx;LogFileName=<run>.trx' --results-directory docs/development/evidence/WP02-11/implementation`; complete console output is preserved in the matching `.log`.

Final exact-byte manifest: [coord-final-after-fingerprints.json](implementation/coord-final-after-fingerprints.json), SHA-256 `6f99832ec575b7507ae444bde0c958fbd4155c93ea5aff75ac929c5486861a6b`. All **249 source/config/lock**, **8 restored dependency**, and **25 compiled-artifact** entries were checked against disk. Final before/after source and dependency arrays are identical. The artifact inventory includes the preserved historical ArchitectureTests output; it is not a new architecture execution. Compilation only rebuilt dependencies required by the affected project.

Final combined TRX SHA-256: `379d8e823e746b64466ee9a33e93730c8e907d8b8d83a6ed661c2a2d282551cd`. Individual run/TRX/log hashes and actual XML counts are in [coord-final-evidence-check.json](coord-final-evidence-check.json). Per-issue before-RED, before-GREEN and after-GREEN snapshots identify the distinct branch/source bytes; a before-GREEN artifact snapshot is still the preceding RED assembly, as stated in each issue report. Source SHA and working-byte hashes are separate identities; CRLF Git checkout normalization is not called a new execution.

## Reuse and remaining delivery gates

Original `final-legacy-http-gateway-auth.trx` (98/98) and `final-architecture.trx` (98/98) remain historical executions with `after-final-fingerprints.json`. Neither is relabeled as run against current binaries. HTTP corrections touch only v2 internals/private tests: legacy behavior, public signatures, project dependencies and architecture boundaries are unchanged. Their successful unaffected assertions are reused under that scope. The new baseline also contains WP02-07; its own integration/architecture evidence remains separately owned by COORD, not inferred from this task's older 98 results. The combined local HttpV2 run validates current HTTP behavior with current necessary dependencies.

All original 42 business AC remain NOT_RUN; the original 38 partial library mappings and four host-only NOT_RUN rows are not promoted to business acceptance. No real Host/Owner binding is enabled; no remote receipt/permission/retention, package adoption or exactly-once proof is claimed.

Both Issues stay Open until remote main contains the verified fixes. This worker performed no push, PR, protected-main merge, shared status update, Actions trigger/rerun/cancel, DB, package publication or deployment. COORD owns the targeted independent recheck, current-main drift check, workflow/queued-run checks, normal remote integration and issue closure. The original review is frozen in the external planning reports; its two blockers are addressed by these corrections and remain subject to that targeted recheck.
