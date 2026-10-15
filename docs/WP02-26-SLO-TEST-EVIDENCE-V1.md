# WP02-26 SLO qualification and test evidence V1

This is an opt-in library and TEST_ONLY implementation. `ProductionSloClaimed` and `IndependentlyAccepted` are always false. Local candidate threshold comparisons, real PostgreSQL fixture results, and original business acceptance are separate evidence. The original 32 OBS and 24 TEST business assertions remain NOT_RUN; Main/CRM adoption, production SLO authenticity/approval/retention, remote collectors, and actual business Owner receipts are not established here.

## Compatibility and scope

`CP6.Platform.Contracts.SloQualificationV1` has no added dependencies. `CP6.Platform.Testing.EvidenceV1` remains repository-only under the existing nonpackable Testing project. Existing `Cp6SloEvidenceDocument`, its 1.0.0 JSON schema/evaluator, telemetry accessor types, and HTTP fault handlers are unchanged. The WP02-26 test project source-links the existing TestingUtilityTests and SloEvidenceContractTests for focused regression coverage; this is a library source test, not published-package consumer acceptance.

Call `SloQualifier.Qualify` explicitly before considering any legacy `Evaluate` result. Its result is a distinct qualification result; invoking the old evaluator alone retains historical comparison semantics and does not confer new qualification. The new gate does not coerce its arbitrary-precision result into the old decimal wire representation or silently rewrite a legacy document.

## Frozen input and qualification

`FrozenArtifact` accepts caller-supplied bytes and a pinned lowercase SHA-256, verifies the actual bytes, owns a defensive copy, and exposes copies only. Each artifact is bounded to 4 MiB; JSON depth is 32; duplicate, unknown and missing JSON properties are rejected; nullable fields must be explicitly present with null when inapplicable. There is no file-path, URL-fetch, remote query, or resolver capability. The caller must obtain bytes through its controlled local input path.

Four independently supplied artifacts use schemaVersion 1.0.0:

- `SloDefinition`: version, method, unit, query digest, exclusion rule, exact threshold and direction, plus percentile or gauge interval/gap settings.
- `QueryBinding`: literal query/expression, named population, denominator rule and units/exclusions. The expression is frozen provenance; this library does not execute a query or prove producer authenticity.
- `PopulationInventory`: independently obtained population name/authority reference, UTC window and expected member keys, timestamps, release identities, exclusions and weights.
- `ObservationSource`: immutable export of observation keys, timestamps, release identities, units, exclusions, values and weights; definition/query/inventory digests; closed UTC window and seal; completeness/dropped/overflow status.

`QualificationBinding` comes separately from the trusted caller and pins the expected inventory hash/authority, definition/query hashes, release digest and window. Binding source row count back to itself is insufficient: exact membership and every inventory timestamp, weight, exclusion and release must match. This boundary verifies content consistency, not independent-source authentication. A caller supplying fabricated inventory and bindings still has only a local fixture result and cannot obtain independent/production acceptance.

Methods have explicit numeric semantics:

| Method | Recalculation | Additional checks |
|---|---|---|
| ExactRatio | Exact weighted K/N from 0/1 observations and positive integer inventory weights; percent multiplies by 100 | unit is ratio or percent; denominator is included-inventory-weights |
| WeightedNearestRank | Sort raw values; select first cumulative weight reaching ceil(p * totalWeight / 100) | p in (0,100], explicit positive integer weights; no averaging percentiles |
| GaugeMaximum | Maximum exact raw sample on declared UTC sampling grid | unit weights, interval and maximum gap, full grid and tail coverage |

Exclusion algorithms are explicitly `none` or `inventory-declared`; the latter uses the separately bound inventory membership/exclusion decision and does not execute arbitrary predicates. All arithmetic/comparison uses BigInteger numerator/denominator. No product SLO threshold is invented: tests use clearly synthetic fixture definitions. Missing/empty population, incomplete capture (including WP02-25 Incomplete), dropped/overflowed samples, mixed releases, unknown method/exclusion algorithms, missing or mismatched units, unsupported ratio units, altered query/denominator/exclusions, sample gaps, duplicate members and invalid numeric tokens are Indeterminate. Exact numeric tokens have resource bounds (16,384 characters and exponent magnitude), not newly declared financial legal-input limits. A resource rejection cannot be called a business-contract rejection.

`QualificationJournal` is an append-only in-memory journal capped at 1,000 entries and 64 MiB of retained artifact bytes. IDs cannot be reused. Corrections require a new ID, increasing version and a supersedes reference in the same window/release/inventory-authority lineage. Query, definition and inventory bytes may change in the new entry; the old entry retains all original bytes and results. Window, release or authority changes form a new lineage. It is not durable retention or an independent evidence registry; a host must persist exported bytes with its retention policy.

## Exact assertions and provenance

`ExactNumber.Parse`, `FromRational`, arithmetic and `NumericEquals` operate on exact values. Coefficient and scale preserve parsed decimal representation; nonterminating rationals remain numerator/denominator. `ExactAssert.Equal(string,string)` and `Equal(ExactNumber,ExactNumber)` compare numeric values; `BytesEqual` compares representation bytes separately.

`ExactValue.FromUtf8` rejects oversized byte spans before decoding and owns original UTF-8 bytes and lexical token. `FromClr<T>` records the actual CLR kind, exact value, lexical rendering, and source representation (native IEEE/decimal bits; invariant integer text). IEEE float/double values are decoded into exact binary rationals, never reinterpreted as exact decimal literals; NaN/infinity cannot qualify. CLR values did not originate as wire tokens, so their rendered lexical text is not claimed to be original wire syntax. Signed zero and trailing zeros may be numerically equal while lexical/native bytes differ.

Input provenance:

- S01-03 accepted Main implementation: `b24f76445ffdc9b4df0de77eb394a6aa47726a0c` (CreditAmount BigInteger coefficient/scale).
- S01-04 accepted Main implementation: `bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8` (inventory lexical/exact/rational separation).
- Wave7 assigned Main input baseline: `1129953156b7775152a74ad2f51cb95ef430ee56`; assignment/admission remains an input, not business adoption.
- Frozen `inputs/vectors.json` SHA-256: `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`. The test reads actual pinned bytes and consumes existing accepted coefficient/scale vector pairs. Embedded source_pins retain individual upstream paths, blob IDs and hashes.

There is no production reference to Main, no new financial profile codec and no assertion that generic numeric parsing enforces native financial lexical constraints. Tests include >2^53, beyond System.Decimal capacity, FIN-CREDIT 38/8 and FIN-INV 18/12-scale examples, signed/trailing zero, arbitrary exponents and nonterminating rationals. Only generic exact arithmetic/representation is claimed.

## Bounded capture and DAG

Construct `EvidenceCase(caseId, limits, timeProvider, activitySources, meters)` before operations. Capture is explicit: matching `cp6.case.id` and a unique `cp6.operation.key` are required. Unrelated cases are ignored; an explicit cross-case record, duplicate/replayed key, mutated case tag or cross-case/dangling graph edge invalidates this case. Instruments preserve actual CLR kind through typed MeterListener callbacks. Source inventories are limited to 128 unique names of at most 256 characters each.

Count, bytes, elapsed time and in-flight activity limits are enforced. Actual metric tags, activity tags, links and link tags are checked before copying: at most 128 entries each, with conservative escaped-text byte budgets. Primitive diagnostic tag values are rendered invariantly; unsupported custom objects are rejected without invoking arbitrary ToString code. Metric tag snapshots are retained. The byte budget includes original numeric bytes, conservative text/metadata charges, and allocation-free BigInteger GetByteCount charges for numerator, denominator and coefficient (shared backing counted conservatively). It is a bound on retained evidence representations, not an exact CLR heap profiler, and does not change native financial legal-input limits. Errors stop accepting further evidence and stay visible. `Complete(expectedOperationKeys, expectedEdges)` verifies a separately supplied exact expected inventory, requires all activities stopped, validates the full actual DAG, then shuts down listeners and executes cleanup before returning final state. Disposal before completion, observer exceptions, cleanup failures, overflow and missing operations prevent Complete. Completion validation exceptions become visible Incomplete results and always execute listener cleanup. A completion-validation flag and a cleanup-finished flag prevent reentrant cleanup/observer callbacks from publishing an early Complete snapshot. Call `Snapshot` after completion/disposal to inspect final evidence. Snapshots contain read-only lists and owned exact values.

Expected edges distinguish Parent, Link and Ancestor. Parent and Link declarations must match actual direct edges exactly; Ancestor declarations additionally require reachability. All intermediate spans remain present, links retain trace/span identities, flags, trace state and link tags, cycles/dangling/missing edges and duplicate span identities fail. DAG traversal is iterative, with finite node/edge/traversal budgets; exceeding a budget is incomplete evidence, not a successful truncated graph. Tests exercise fanout, distinct retry attempts, an intermediate Dapr span, receive and apply with links. `LocalSequence` orders observation/stop callbacks only; it is never database commit order or proof that a technical success committed business writes.

## Deterministic fault harness

`DeterministicFaultCase` owns `CaseClock` (virtual UTC/monotonic time) and `CaseRandom` (seeded xorshift32), a finite copy of outcomes, explicit retry placement and capped consumed script entries/terminal responses. Exhausted attempts remain explicit errors, never successful calls. Its only terminal is a named `in-memory:` finite status sequence; it accepts no arbitrary handler, endpoint or network fallback. This is a library fixture pipeline, not production HTTP middleware or a fault endpoint.

`RetryOutsideFault` evaluates the script per retry; `RetryInsideFault` evaluates a fault once around terminal retry. Retry policy is explicit: up to the configured additional attempts for 5xx statuses only; thrown exceptions remain visible. Status/Throw never call the inner terminal. Success/Delay call it; Delay advances virtual time. Cancellation is checked before script and before inner work. Exhausted script/terminal, cancellation and execution failures remain Incomplete; observer errors do not change the returned terminal business result but invalidate evidence. `Complete(expectedScriptCalls, expectedTerminalCalls)` verifies call counts and performs cleanup before final state. Complete during active execution is rejected as Incomplete; closing/disposal from an observer stops the inner terminal. Cleanup snapshots cannot report Complete before cleanup finishes. No wall-clock or process-global random state is used by the harness.

## Evidence and reproduction

Run only the task test project and appropriate architecture/PG gates with SDK 8.0.424. Each run uses a fresh evidence directory under `docs/development/evidence/WP02-26/20261015T001945Z/implementation/`, containing exact command, immediate numeric exit status, output and TRX when the test host executed. Initial `unit-red-01` preserves four actual capability assertion failures. Subsequent failures are retained; build failure is never called a failing behavioral test.

Native PostgreSQL tests and resource ownership/cleanup are documented by the task's separate execution evidence. Real server crash/restart and observed SQLSTATE must remain distinct from injected application ACK suppression; this library cannot turn either into original Owner/production acceptance.
