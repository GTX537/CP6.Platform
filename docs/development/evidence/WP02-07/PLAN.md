# WP02-07 Implementation Plan

> Execute inline with executing-plans; one concentrated independent review at delivery. This task-specific location and cadence follow the approved wave5 window.

**Goal:** Add a caller-owned PostgreSQL Inbox seam with content admission before every outcome, atomic Owner/technical writes, immutable original-result copies, and read-authorized commit recovery.

**Architecture:** An opt-in `CP6.Platform.EntityFramework.InboxV1` API receives the actual caller DbContext/DbTransaction and current WP02-01 trusted context. A trusted host adapter supplies its existing lock protocol and reads its original authority/journal; Platform executes and verifies native transaction locks on that exact connection, never opens or commits a transaction. New technical tables are isolated from legacy retention; Owner business journals remain authoritative.

**Tech stack:** net8/EF8.0.30; task-private Npgsql8.0.8/provider8.0.11; real PostgreSQL18.6 at reserved55427. No shared project, dependency, workflow or legacy API edits.

## Frozen additive public surface (before production code)

All following types live only in InboxV1; overload names may use normal .NET cancellation defaults. New types carry XML documentation of trusted adapter boundaries.

```csharp
enum PreCheckpointIdentityDecisionV1 { TransportIdentityConflict, FreshCandidate,
  TransportDuplicate, OlderVersionIgnored, SameVersionEqual, SameVersionConflict, DependencyUnavailable }
enum InboxLockStageV1 { Registration, Reconciliation, Published, Projection, Inbox, Checkpoint, Observation }
record InboxLockV1(InboxLockStageV1 Stage, long Key);
record InboxLockPlanV1(string Profile, string WriterSetReference, IReadOnlyList<InboxLockV1> Locks);
record InboxContractBindingV1(string Owner, string OperationKey, string RequestVersion,
  string InputDomain, string InputCodec, string InputDigest, string EventType,
  string Source, string Region, string SchemaIdentity, string BundleIdentity, string RegistrationReference);
record InboxAuthorityVersionV1(Guid TenantId, string AggregateId, int Version,
  string EventType, string DataSha256, string SourceReference);
record InboxAuthoritySnapshotV1(bool Available, InboxAuthorityVersionV1? Current,
  IReadOnlyList<InboxAuthorityVersionV1> History);
record InboxOwnerResultV1(string NativeReceiptReference, ReadOnlyMemory<byte> OriginalResult);
// Immutable copied input and result bytes; constructors internal for processor-minted types.
class InboxInputV1 { /* frozen delivery, contract binding, raw/data hashes and exact original bytes */ }
class PendingCommitResult { /* receipt id, original key, decision; deliberately no transport mapping */ }
class CommittedDeliveryOutcome { /* persisted receipt, read-filtered result, decision and transport outcome */ }
interface ICp6InboxPreCheckpointV1 {
  ValueTask<InboxLockPlanV1> GetLockPlanAsync(InboxTransactionV1 tx, Cp6InboxDelivery delivery,
    InboxTransportV1? original, CancellationToken ct);
  ValueTask<InboxContractBindingV1> ValidateContractAsync(InboxTransactionV1 tx,
    Cp6InboxDelivery delivery, CancellationToken ct);
  ValueTask<InboxAuthoritySnapshotV1> ReadAuthorityAsync(InboxTransactionV1 tx, InboxInputV1 input, CancellationToken ct);
  ValueTask<InboxOwnerResultV1> ApplyDecisionAsync(InboxTransactionV1 tx, InboxInputV1 input,
    PreCheckpointIdentityDecisionV1 decision, InboxTransportV1? original, CancellationToken ct);
}
interface IInboxResultReaderV1 {
  ValueTask<ReadOnlyMemory<byte>> AuthorizeAndFilterAsync(InboxTransactionV1 tx,
    InboxDeliveryOutcomeV1 outcome, InboxOriginalResultV1? original, CancellationToken ct);
}
class CallerTransactionInboxV1 {
  CallerTransactionInboxV1(ITrustedContextAccessorV1 context, ICp6InboxPreCheckpointV1 owner);
  Task<PendingCommitResult> ProcessWithPreCheckpointV1(DbContext db, DbTransaction transaction,
    Cp6InboxDelivery delivery, CancellationToken ct = default);
  Task<CommittedDeliveryOutcome?> ReadOriginalResultAsync(DbContext db, DbTransaction transaction,
    string owner, string operationKey, string requestVersion, string inputDomain, string inputCodec,
    string inputDigest, Guid receiptId, IInboxResultReaderV1 reader, CancellationToken ct = default);
}
class InboxTransactionV1 { /* actual Context, Connection, Transaction; server identity; checked commands */ }
static class InboxModelV1 { void AddCallerTransactionInboxV1(this ModelBuilder model, string schema); }
// New EF entities: InboxTransportV1 (unique consumer/message), InboxCheckpointV1,
// InboxOriginalResultV1 (unique tenant/owner/original key/version; immutable technical copy),
// InboxDeliveryOutcomeV1 (append-only receipt per evaluated delivery, nullable original-result ref).
```

Contract validation is a trusted host service, called under the registration protection, never a public `alreadyValidated` boolean. The library separately extracts root `data` bytes directly from original UTF8 JSON; it cannot accept a caller-provided raw/data hash. Owner canonical input digest has an explicit separate domain/codec and is never synthesized from raw hash. The adapter must use S01-09's exact contract catalogue and source registration; source/package adoption remains host responsibility.

The concrete V1 lock profile is PostgreSQL transaction-scoped **advisory** locks using the Owner's existing exact key inventory, not a second lock namespace. Exactly seven ordered, distinct keys are acquired on the caller connection. Pre-existing advisory locks are rejected rather than claiming unverifiable prior order; the adapter delegates its original acquisition here at the start of the transaction. Inspect `pg_locks` for actual granted ExclusiveLock ownership after acquisition and before/after Owner writes; verify native transaction id/backend/database/principal throughout. Hosts using row-lock or other protocols need a separately reviewed adapter/profile and stay disabled. The fixture proves only its own shared-writer protocol. No arbitrary bool, tx-id string or ContextEvidence is lock evidence.

Initial read of the original transport selects the trusted original aggregate lock roots; after locking, reread and require unchanged binding. Foreign-tenant collisions never disclose or mutate foreign original state. Transport identity conflict has priority; known historical same-version conflicts precede duplicate/older outcomes. The source snapshot is read by the Owner adapter under those locks, not constructed from incoming content. A mismatch between a Fresh decision and a checkpoint already at/above that version closes the path.

Process requires Serializable caller transaction, no ambient transaction, the exact active EF transaction/connection and trusted tenant. It never catches uniqueness/serialization failure to independently write or return duplicate. Any failure requires caller rollback and retry of the **whole original input** in a fresh transaction through the same gate. Owner callbacks cannot be sandboxed from arbitrary in-process code; runtime identity checks detect ended/replaced transactions, and host adoption must audit all writers and prohibit commit/remote effects inside callbacks.

Process saves the Owner tracked changes + new technical rows in the caller transaction, never commits. It returns no result bytes/transport-success capability. Caller commit confirmation is observed by a separate read-only ReadCommitted transaction using the original receipt/key/digest and current read policy. If not visible return null (unknown, not proof of rollback); field-filtering preserves original stored bytes/hash. Read authorization is distinct from a new-write grant. Caller can use the same recovery query after lost commit acknowledgment. Original-result rows are append-only and outside old retention entity types. New uint xmin mapping is exercised with real PostgreSQL optimistic concurrency; no byte[] rowversion assumption.

## Implementation files and execution

### Pre-delivery surface clarification (2026-10-13, before adding the method)

Lost HTTP responses can lose the pending receipt id as well. Add `ReadOriginalOperationAsync(DbContext, DbTransaction, owner, operationKey, requestVersion, inputDomain, inputCodec, inputDigest, IInboxResultReaderV1, CancellationToken)` returning `CommittedOriginalResultV1?` with only original key, filtered bytes and original hash, **no transport mapping**. It queries the immutable Owner-key slot in a READ ONLY transaction. Delivery confirmation still requires its precise receipt id; reading an old business result cannot acknowledge a newly conflicting delivery. Add original-operation binding digest to the immutable transport identity so an unchanged message cannot be rebound to a replacement Owner operation.

- [ ] `InboxV1/InboxContractsV1.cs`: closed decisions, trusted adapter interfaces, immutable byte-carrying inputs/results and safe exceptions.
- [ ] `InboxV1/InboxTransactionV1.cs`: actual native binding, readonly/write isolation checks, native identity checks and ordered lock execution/proof.
- [ ] `InboxV1/InboxModelV1.cs`: four opt-in technical entities/mappings, uint xmin and independent tables. Conflict outcomes retain evidence separately and cannot overwrite or mint an Owner result; same original key with different input fails before a Fresh effect.
- [ ] `InboxV1/CallerTransactionInboxV1.cs`: strict original-byte extraction, content classifier, owner same-transaction write set, pending result and readonly recovery.
- [ ] `tests/...UnitTests/InboxV1/InboxContractTests.cs`: executable reflection RED for absent API, then seven-decision classifier, hash domains/byte preservation, malformed data, locked-order/public pending shape.
- [ ] `tests/CP6.Platform.WP0207.PostgreSqlTests/`: nonpackable private project, imported central versions, guarded environment/provision helper, fixture Owner/projection/journal/conflict tables and real transaction scenarios.
- [ ] `docs/WP02-07-CALLER-TRANSACTION-INBOX.md`: host integration, disabled adoption boundary, full retry/unknown/read protocol.
- [ ] Evidence: all60AC mapping from fixed source, source/dependency/artifact manifests, actual TRX/logs, failure evidence, database ownership and identity, review and delivery.

Write tests before each behavior and retain expected RED. First unit test uses assembly reflection so missing feature is a failing assertion, not a compiler error. Real PostgreSQL tests assert commit/rollback of every fixture table, strict transaction/tenant rejection, immutable raw/result bytes, all decisions, native locks and shared-writer contention, same-key parallelism, 40001/23505 recovery through a new gate, readonly original query after suppressed commit confirmation, new-write denial with allowed read, and real xmin concurrency rejection. No InMemory/SQLite, skips or synthetic business-acceptance claims.

## Required local commands

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --filter FullyQualifiedName~InboxV1 --logger 'trx;LogFileName=inbox-unit.trx'
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.WP0207.PostgreSqlTests/CP6.Platform.WP0207.PostgreSqlTests.csproj --logger 'trx;LogFileName=inbox-postgresql.trx'
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --logger 'trx;LogFileName=inbox-architecture.trx'
```

One task-level source/permission/transaction review after these tests; only affected fixes retested. Source input change or new risks expands validation with explanation. Publish only task branch and append-only Handoff delivery after exact workflow-filter checks. COORD owns shared state, serial main merge and remote inclusion check. No package publication, legacy data migration, Actions or deployment.
