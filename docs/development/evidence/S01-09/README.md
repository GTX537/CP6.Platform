# S01-09 Platform native routing and original bytes

Implementation source: `708253360269396a43c2b25cce1c87cd9f59f9d2`, based on Platform `30bd23af6808d217a23878bd9437513043c52834`. This is a task candidate for COORD's serial integration. Main roadmap/state synchronization and remote-main delivery receipt remain the coordinator's responsibility.

`Cp6NativeContractCatalog.LoadS01(manifestBytes)` accepts only `CP6-S01-CONTRACT-INPUTS/1.0.0` at the compiled manifest SHA256. `ResolveOpenApi` selects `crm-inq-r3-v15` using the exact OpenAPI version/title/info version/schema root/hash and real asset bytes. `ResolveRegistry` selects `erp-credit-current-v1` or `erp-credit-frozen-r8` using the exact codec namespace, registry filename, hash and actual bytes. Profiles are caller configuration outside the native wire envelope. Unknown profiles/versions, cross-use and changed assets fail. No CloudEvent multi-version adapter is needed: these native identities are not CloudEvents, and its original one-entry-per-event-type behavior remains.

`Cp6ContractBundle.Load(root, expectedBundleVersion, expectedManifestSha256)` adds explicit pinning to the existing loader; obtain both expected values from trusted deployment configuration. The original overload remains compatible. Both retain exact event type/schema ID/schema version matching and schema/example hash checks. Existing schema metadata and five-example requirements remain intact. Duplicate manifest/business keys reject, including escaped equivalents. P08's accepted behavior for duplicate root `traceparent`/`tracestate` remains: tracing is discarded without rejecting business delivery. This exception does not apply to native JSON or nested business data.

`Cp6OriginalJson.Capture(utf8)` creates an owned strict JSON snapshot. `GetBytes()` returns a defensive copy for future transport/storage, `Sha256` hashes the exact input and `Matches(bytes)` compares bytes. CRLF, whitespace, key order, Unicode and numeric spelling are preserved. `Cp6NativeJson.SemanticallyEquals` is a separate schema-value operation; it handles arbitrary precision without decimal/double and never returns normalized wire bytes. `RequireNullProperty` checks presence and actual JSON null as a field projection. Parsing rejects invalid UTF8, comments, trailing commas and duplicate decoded names at every native object level, with the existing depth-64 parser boundary.

## Local evidence

- [Input provenance](input-provenance.json): exact Main Git blobs from `dfc043b1473dcea51d34ff87a7808692349c278a` and three real native asset hashes. Test fixtures retain original bytes via scoped attributes and are copied to test output. Tests do not read archive or Main working directories.
- [Validation report](validation.json): all 35 vector IDs and actual outcomes, source/compiled artifact hashes, test times, workflow checks and unexecuted scope.
- [Task review](review.md): independent complete source diff review, no blocking findings; evidence-only additions verified separately.
- [Final local output](task-pass-output.txt) and [TRX](test-results/task-pass.trx): **110 passed, 0 failed, 0 skipped**, including all designated contract tests and direct MessagingTelemetry/DaprKafka/TraceContext regression consumers. The filter also includes TransactionalMessagingTelemetryTests by its suffix; these are unit tests, not database transaction acceptance.
- Failures remain preserved: `red` (49 expected failures against API scaffolding and existing duplicate behavior), `red-bundle` (missing pinned loader/manifest duplicate rejection; one test initially expected a narrower exception than the existing duplicate-schema rejection), `green-initial` (3 overly narrow JsonException assertions), `red-utf8` (invalid UTF8 string was accepted by lazy JsonDocument parsing), `consumer-regression` (1 regression against existing P08 duplicate tracing policy, corrected without changing that policy). `contracts-pass` is the earlier narrower success, superseded by the final 110-case task run.

The task-pass execution HEAD was the baseline with working-tree changes; the unchanged tested source was then committed as the source SHA above. The report verifies that binding and does not relabel a commit as a new test execution. SDK 8.0.424 was already present in the user's dotnet installation; the default system dotnet had only SDK 10.0.302. No project SDK, dependency version, TFM or shared configuration changed.

Reproduce from a machine with the repository's .NET 8 SDK and normal package restore available:

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --filter "FullyQualifiedName~CloudEventContractTests|FullyQualifiedName~NativeProfileContractTests|FullyQualifiedName~MessagingTelemetryTests|FullyQualifiedName~DaprKafkaContractTests|FullyQualifiedName~TraceContextContractTests"
```

`verify-delivery.py` checks retained evidence/bytes/current publication paths; it does not rerun tests. `import-inputs.py --main-repo <CP6 clone>` is only for reproducing source imports with the pinned archives/derived evidence available; ordinary testing does not need it.

## Scope of acceptance

All results are local contract or field subassertions. Route resolution does not validate an entire native command, instantiate an ERP codec, grant Owner authority or establish runtime adoption. The constructed BYTES inputs prove byte retention only. Original input package execution flags remain unchanged. Full INQ/ERP business cases, Inbox/Outbox atomicity, permissions, database persistence/migration, current/frozen business mapping, real replay, downstream package adoption and deployment are **NOT_RUN**. No NuGet package was published. PostgreSQL remains the sole target for subsequent database work.

No Actions run was started or cancelled and no workflow setting changed. Both branch/main workflows must still be rechecked before each publication: seven are manual only; RFC3161 preflight's PR filter is limited to its workflow and ReleaseTests file, neither within this task diff. Main protection still requires the normal PR path; there is no bypass authorization.
