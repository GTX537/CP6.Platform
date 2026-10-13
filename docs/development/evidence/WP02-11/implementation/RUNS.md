# WP02-11 local implementation verification

Workspace: `D:/CP6.Platform-worktrees/wp02-11-20261013`; branch `codex/wp02-11-http-resilience-20261013`; baseline `9259a45d39bae7e98ca804a74778a588c1ad4619`. This evidence is for uncommitted candidate working bytes; the parent delivery binds the final commit. No commit/push, Actions, database, package publication, environment trust change or deployment was performed by this implementation worker.

Every .NET invocation used `C:/Users/tt/.dotnet/dotnet.exe`, SDK **8.0.424**, .NET/ASP.NET runtime **8.0.30**, Release, Windows x64. Full environment is `sdk.txt`. The system SDK10 was not used and global.json was unchanged. No tests or gates were skipped or weakened.

## Current inherited-cancellation verification

The targeted recheck of the same cancellation/source finding exposed a remaining case: a parent deadline's original caller cancellation was flattened into a timer-like total cancellation. `Cp6HttpDeadline` now retains each original caller token and recursively exposes its source to the handler; ancestor caller cancellation propagates `OperationCanceledException`/Cancelled, while ancestor timer expiry remains TotalTimeout. No public API or legacy behavior changed.

`parent-cancellation-red.trx` is **4 discovered, 1 passed, 3 failed, 0 skipped**: cancellation during send, wait and original-result query through two deadline levels failed, while the parent-timer control passed. After the targeted fix, `parent-cancellation-http-v2.trx` is the **current** affected-suite gate: **150 discovered, 150 passed, 0 failed/skipped**. The affected suite ran once for this fix. The previous 146-case passing run and all its artifacts remain historical and unchanged.

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter 'FullyQualifiedName~InheritedCallerCancellation|FullyQualifiedName~InheritedTimerExpiry' --logger 'trx;LogFileName=parent-cancellation-red.trx' --results-directory docs/development/evidence/WP02-11/implementation
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter 'FullyQualifiedName~HttpV2' --logger 'trx;LogFileName=parent-cancellation-http-v2.trx' --results-directory docs/development/evidence/WP02-11/implementation
```

`before-parent-cancellation-fix-fingerprints.json` captures the 146-case candidate before these edits. `before-parent-cancellation-green-fingerprints.json` captures corrected source immediately before the affected suite; its source array matches `after-parent-cancellation-fix-fingerprints.json`. The latter is the current 236-source/8-dependency/25-artifact manifest. Before-green compiled artifacts are from the preceding build, while after-fix compiled artifacts bind the passing 150-case suite; no equivalence between those assemblies is claimed. The original legacy and architecture 98/98 results remain reused under the unchanged scopes and provenance described below.

SHA-256: current 150-case TRX `402b5db617ae39264b1850343a9820c02ad4d995dfdc911c66ff9941a31860eb`; inherited-cancellation RED TRX `c5b95175201e8af75f53c23d3057b421bb58926898d881f786d18bf6084c56bc`; current manifest `5147dd29f33bfbcc8f82e4de9c32b381eb3e700a7066fa4f8d8ef82f64565341`.

## Previous review-fix verification (historical 146-case candidate)

The fresh task-level specification review found two P1 issues in the original candidate: response stream `IOException` bypassed the typed transport/Unknown recovery path, and already-completed authority tasks could return after cancelling or exhausting their deadline. Both were fixed in `Cp6HttpV2Handler.cs`. Response-body IO now enters the existing transport classification (including original receipt query for verified writes); authority/query/body completions recheck caller/parent cancellation and the applicable total/child deadline before interpreting or returning a result. Late responses are disposed.

`review-fixes-red.trx` preserved **16 discovered, 5 passed, 11 failed, 0 skipped**. It reproduces both issues, including an actual Kestrel response truncated after the client observed headers. `review-fixes-http-v2.trx` records that candidate's affected-suite result: **146 discovered, 146 passed, 0 failed/skipped**. It includes all original 128 cases and 18 added review-boundary assertions. Two additional replay-authorization expiry cases were added after the initial RED run; they are not claimed as separately observed RED.

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter 'FullyQualifiedName~ReviewRegressionsTests|FullyQualifiedName~RealTruncatedBody' --logger 'trx;LogFileName=review-fixes-red.trx' --results-directory docs/development/evidence/WP02-11/implementation
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter 'FullyQualifiedName~HttpV2' --logger 'trx;LogFileName=review-fixes-http-v2.trx' --results-directory docs/development/evidence/WP02-11/implementation
```

Matching console logs are retained. The affected suite ran once after these fixes. No legacy or architecture suite was rerun solely because the AspNetCore assembly changed: their unchanged implementation/test/configuration scope reuses `final-legacy-http-gateway-auth.trx` (98/98) and `final-architecture.trx` (98/98), with original `after-final-fingerprints.json` and the parent's `../independent-evidence-check.json` observation. These are **historical runs on the original candidate**, not runs of current rebuilt binaries.

`before-review-fixes-fingerprints.json` records the original candidate before adding the review tests/fixes. `after-review-fixes-fingerprints.json` records the current tested working bytes and artifacts after the passing affected suite: 236 source/config/lock files, eight restored dependency manifests and 25 compiled artifacts. Its source set intentionally differs from the before set in exactly three paths: the handler, `HttpV2/LoopbackTests.cs`, and added `HttpV2/ReviewRegressionsTests.cs`. A separate pre-green snapshot was not captured; none is claimed. Original before/after-final manifests and all original TRX/logs were preserved. Updated documentation/evidence is not a compiled input.

SHA-256: current suite TRX `132ff6b933d69f2a0f738af862b23a1e2ed56bea0b54c004fd50c7229c85eb67`; RED TRX `8f267ba08f7f584cbe34375785899bc681ab660df50e1a8a3764fe71c5e5d549`; current artifact/source manifest `b39d80bfbee74c52c065dd142373be51f4863e649510a436551240203c1aaaa6`.

## Original pre-review gates (historical)

| Immutable TRX and matching .log | Discovered | Passed | Failed | Not executed |
| --- | ---: | ---: | ---: | ---: |
| final-http-v2.trx | 128 | 128 | 0 | 0 |
| final-legacy-http-gateway-auth.trx | 98 | 98 | 0 | 0 |
| final-architecture.trx | 98 | 98 | 0 | 0 |

Exact commands, from the workspace root:

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter 'FullyQualifiedName~HttpV2' --logger 'trx;LogFileName=final-http-v2.trx' --results-directory docs/development/evidence/WP02-11/implementation
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter 'FullyQualifiedName~HttpResilienceTests|FullyQualifiedName~GatewayContractTests|FullyQualifiedName~AuthenticationContractTests' --logger 'trx;LogFileName=final-legacy-http-gateway-auth.trx' --results-directory docs/development/evidence/WP02-11/implementation
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj -c Release --logger 'trx;LogFileName=final-architecture.trx' --results-directory docs/development/evidence/WP02-11/implementation
```

Console streams were captured using `2>&1 | Tee-Object` to the matching `.log`. The required legacy regression ran once against the candidate; original legacy/gateway/auth test and implementation files are unchanged except the explicitly additive ProblemDetails overloads. No separate full-repository suite is claimed.

`before-final-fingerprints.json` and `after-final-fingerprints.json` hash exact working bytes for 235 source/test/build/dependency-lock inputs. Their source arrays compare identically. `after-final` includes eight restored `project.assets.json` manifests and 25 compiled CP6 DLL/PDB/deps files, including ArchitectureTests. The before stage preceded the final three gates and did not yet have architecture output; the after stage was captured after all gates. SHA-256 values hash disk bytes, not Git-normalized blobs. `.gitattributes` only mandates LF for `contracts/**/*.json`; Git commit normalization must be compared separately by the parent rather than relabeling these disk hashes. The capture script itself changed only to include architecture artifacts after its gate; it is not a compiled input.

## Preserved development executions

| TRX | Total | Passed | Failed | Meaning |
| --- | ---: | ---: | ---: | --- |
| red-parser.trx | 15 | 1 | 14 | Executable parser stub returned Absent; behavioral RED before parser implementation |
| parser-green.trx | 15 | 15 | 0 | Initial integer/three-date/parser behavior |
| draft-regressions.trx | 84 | 80 | 4 | Caller cancellation in parent-budget wait; unrelated OCE source; unknown empty errors field; floating ceil boundary |
| protocol-parser-red.trx | 114 | 111 | 3 | Unicode RFC850 digits; extreme UTC pivot overflow; leap-second handling |
| integrated-v2.trx | 124 | 123 | 1 | Recovery-query total-deadline callback race classified as unavailable |
| integrated-v2-fixed.trx | 124 | 124 | 0 | Corresponding fix verified |
| transport-boundary-red.trx | 3 | 0 | 3 | Redirect/cookie configuration and actual loopback redirect amplification (51 sends) |
| transport-boundary-green.trx | 3 | 3 | 0 | V2 transport boundary enforced |
| https-fixture.trx | 1 | 1 | 0 | Real HTTPS fixture POST → GET original query → identical POST |

All development test commands use the same SDK/project/config/results directory pattern above with their TRX basename. `red-parser`, `parser-green`, `draft-regressions`, `protocol-parser-red`, `integrated-v2`, `integrated-v2-fixed` used `FullyQualifiedName~HttpV2`. The transport runs used `FullyQualifiedName~RedirectAndCookie|FullyQualifiedName~OptInClientDoesNotFollowRedirect`. The HTTPS run used `FullyQualifiedName~HttpsFixtureOnly`.

`protocol-parser-regressions.log` also preserves an actual compiler failure (incorrect test use of nonexistent `HttpRequestOptions.Get`); it discovered no tests and is **not behavioral RED**. No TRX is claimed for that build failure. Its corrected invocation produced `protocol-parser-red.trx`.

Only the parser's initial RED preceded implementation. Other modules were drafted before the full tests; subsequent failure-first regressions are recorded accurately. This delivery does not claim every behavior followed TDD. Earlier runs did not capture complete contemporaneous source/artifact manifests, so their logs/TRX are historical results, not interchangeable final-byte validation. The final manifests bind the three final gates.

## Self-review and limits

- Reviewed every new HttpV2 source/test and the additive old-file diff; `git diff --check` passed. No shared contracts, project/lock/dependency configuration, old tests, workflows, release files, DB or status files were edited by this worker.
- Disabled remains zero retry, including reads; default write registrations do not acquire authority from headers or DTOs. Exact typed transport snapshots are verified before eligible sends. Original read and new-effect authorization remain separate.
- Reviewed budget/source classification, full response reads, decimal jitter rounding, anchor/wait/admission checks, bounded query and late response disposal. Caller cancellation preserves Unknown after send and does not claim remote rollback.
- Reviewed safe text/field allowlist even for empty arrays, actual serialized bytes, endpoint policy identity, trace/correlation and no-second-body behavior.
- Real Kestrel HTTP validates send counts/cancel/body budget/cap/redirect; real HTTPS validates fixture-only recovery order and identical bytes/key using an ephemeral test-local pinned certificate. Certificate/private key were never written or installed. Delegate fixtures do not prove Owner transactions or production adoption.
- No Main/CRM host adoption, distributed deduplication, real Owner permission/receipt atomicity, production proxy/identity settings, cross-call recovery lifecycle or opaque custom transport retry inventory was verified. Those remain explicit Host/Owner gates.
- Within-call bounded retries are implemented. A new SendAsync after Unknown is not an approved recovery continuation; Host/S6 must retain the original identity, lifecycle budget and policy and query the original result. See `docs/WP02-11-HTTP-V2.md`.

The parent owns original-42-AC mapping, fresh independent reviews, transport/coordination evidence, commit/PR/merge and project status. Local gate success alone is not repository delivery or production acceptance.
