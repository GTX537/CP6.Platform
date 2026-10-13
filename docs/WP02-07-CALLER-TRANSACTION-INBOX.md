# WP02-07 — Caller-owned Inbox and original-result recovery

This opt-in PostgreSQL library seam is `CP6.Platform.EntityFramework.InboxV1.CallerTransactionInboxV1`. The caller owns the **actual** DbContext, connection and transaction. Legacy `Cp6InboxProcessor.ProcessAsync`, its models/enums, Outbox and retention remain unchanged. Main/CRM adoption, their business acceptance and package publication are **NOT_RUN / UNPROVEN**.

## Write and confirm

1. Resolve the authenticated transport through WP02-01's host verifier/current authority. The library requires the live trusted accessor and rejects tenant A/B mismatch before host callbacks.
2. The Owner starts a Serializable transaction on its Npgsql DbContext with the explicitly opted-in `AddCallerTransactionInboxV1(model, schema)` mapping. No ambient transaction or different/ended native handle is accepted.
3. Call `ProcessWithPreCheckpointV1(db, transaction.GetDbTransaction(), delivery)`. The adapter supplies its existing lock profile and exact keys; the library executes Registration → Reconciliation → Published → Projection → Inbox → Checkpoint → Observation transaction locks on that same connection. It validates real backend/database/principal/xid and `pg_locks` ownership, not a supplied proof string.
4. The trusted adapter validates the complete original event/registration using the host's S01-09 catalogue. It reads current Owner lineage/projection/observations under the locks. All duplicate/old/equal routes go through this content gate. The seven decisions are closed and separate from legacy enums. The adapter must apply the required Owner write set, including current final authorization for a Fresh effect and atomic conflict/DRIFT/Published withdrawal/audit for conflicts.
5. Platform saves Owner tracked changes plus Inbox/result-copy/outcome/checkpoint changes on that transaction. It does **not** begin, commit, rollback, dispose, retry or independently record failures. `PendingCommitResult` has no result bytes or transport mapping. Dependency unavailable returns no persisted receipt and advances nothing.
6. The caller commits. Only a visible exact delivery receipt read in a separate caller READ ONLY / ReadCommitted transaction, after current read authorization, produces `CommittedDeliveryOutcome` and its SUCCESS/RETRY/DROP mapping. No visible receipt is **unknown**, not proof of rollback. A new delivery must still run admission even if an old receipt can be read.

The concrete native lock profile implemented here supports Owner **transaction-scoped advisory locks**, delegated at the start of a transaction with no prior advisory locks. It does not infer the order of pre-held row/advisory locks or create a second parallel lock namespace. Row-lock profiles and already-held registration adapters require separate reviewed support; they must stay disabled. Host adoption must inventory and wire every receive/reconciliation/publish/revocation writer to its original shared protocol. A fixture holding seven locks does not prove real host writer coverage.

## Bytes, authority and original keys

Raw-envelope and original root `data` digests are calculated independently from exact UTF8 slices, preserving whitespace, escapes and numeric text. Duplicate JSON member names are rejected, including escaped names. Owner canonical input uses an explicit separate domain and versioned codec; incoming metadata cannot self-attest a trusted source. Identity includes consumer/message, tenant/topic/partition/aggregate/version, native schema/bundle/source/region, hashes and the immutable original-operation binding.

Transport conflicts take priority even if content dependencies are unavailable. Known historical content conflicts take priority over Duplicate/Older. Same-version equal content with a different legitimate message/raw envelope can record its own transport observation without repeating effects or clearing drift. A foreign-tenant global message collision records only a technical conflict in the current tenant; it never calls an Owner mutation with an incoming-selected quarantine target. Recovery of that foreign binding needs the original Owner's separately authorized investigation.

`InboxOriginalResultV1` is an immutable **copy** tied to tenant + original Owner/key/request-version + domain/codec/digest and native receipt reference. The Owner journal remains authoritative. Existing-key input mismatch or changed original result/reference is rejected. ERP CommandInbox is not replaced and must not be routed through projection checkpoint semantics. `InboxDeliveryOutcomeV1` keeps conflict outcomes distinct from original results; no conflict overwrites an earlier result.

## Unknown commit and retries

After connection loss/cancellation/commit uncertainty, retain the original operation and exact input. Use `ReadOriginalResultAsync` with its exact pending receipt to confirm that delivery. If the entire response and receipt id were lost, `ReadOriginalOperationAsync` queries the original Owner key/version/digest, applies **current read and field policy**, and returns `CommittedOriginalResultV1`; this type deliberately has no transport acknowledgment capability. Historical read must not demand a new-write grant, old eligibility or old ETag. Missing evidence stays unknown; do not generate a replacement key.

SQLSTATE 23505 and 40001 propagate to the caller. Roll back the entire transaction and discard that DbContext, then re-enter the same complete admission path with the original key and bytes in a new transaction. Do not catch a uniqueness failure and independently return Duplicate. Retry budgets and HTTP/Dapr mapping belong to the host, and no remote non-rollbackable effect may run inside Owner callbacks.

The adapter is trusted in-process code and has the caller's DbContext. Runtime handle/lock checks detect ended/replaced transactions; the library cannot sandbox an adapter that deliberately commits through arbitrary code. Host integration must audit that behavior and all direct SQL, final authorization and write-set coverage. A bool or authentication context is not a held final authorization fence.

## Storage and retention

Four new tables use an explicit Owner schema, unique transport/original-operation/checkpoint keys, a restrictive result FK, and Npgsql's real generated `uint` xmin concurrency mapping. No legacy `byte[]` rowversion mapping or existing migration was changed. No automatic migration, table creation, schema-per-tenant adoption, package publication or retention is enabled. Existing technical retention addresses different entity/table types and cannot select these records. Full retention/hold/requeue/lease governance is a separate task.

Owners must provision the schema, least-privilege access, immutable journal/observation protections, raw replay-source retention and compatible migrations before enabling this profile. Schema alone does not replace TenantId or authorization. These records contain result bytes and require Owner data/field/PII policy.

## Verification

See [PLAN](development/evidence/WP02-07/PLAN.md), [full original60AC mapping](development/evidence/WP02-07/acceptance-map.json) and [delivery evidence](development/evidence/WP02-07/REPORT.md). Tests use a private PostgreSQL18.6 cluster at 127.0.0.1:55427 with newly generated SCRAM credentials, minimal runtime role, owner marker, system identifier, actual directory, DB OID and principal checks. The test helper resets **only its owned fixture schema**, after identity and zero-other-session checks; no existing application database is connected. Logs preserve failed runs. Commit-confirmation loss is an explicit application fault injection after a real COMMIT, not a claimed real network outage.

To reproduce: run the task-private `pg_resource.py create`, set `CP6_WP0207_SETTINGS` to the printed private settings file, then run the three commands in PLAN. Missing owned resource fails rather than skips. `pg_resource.py stop <settings-path>` verifies identity and zero unexpected client sessions, requests smart shutdown and retains data/logs. It performs no recursive deletion. The fixture Owner/authentication/catalogue is synthetic test infrastructure; it is not Main/CRM adoption or production acceptance.
