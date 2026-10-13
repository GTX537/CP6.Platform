using System.Data.Common;
using CP6.Platform.Abstractions.Context;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework.InboxV1;

/// <summary>
/// Opt-in caller-owned PostgreSQL seam. Storage errors propagate: rollback and retry the entire original
/// operation through this same gate. Never catch a uniqueness failure and independently return Duplicate.
/// </summary>
public sealed class CallerTransactionInboxV1(ITrustedContextAccessorV1 context, ICp6InboxPreCheckpointV1 owner)
{
    private readonly ITrustedContextAccessorV1 _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ICp6InboxPreCheckpointV1 _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public async Task<PendingCommitResult> ProcessWithPreCheckpointV1(DbContext db, DbTransaction transaction,
        Cp6InboxDelivery delivery, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var tx = await InboxTransactionV1.BindAsync(db, transaction, _context, false, ct);
        ValidateDelivery(delivery, tx.TenantId);
        // Freeze before any host await; host validation receives its own copy as well.
        var frozen = delivery with { Payload = delivery.Payload.ToArray() };
        var before = await FindTransport(db, frozen, ct);
        var visibleOriginal = before?.TenantId == tx.TenantId ? before : null;
        var plan = await _owner.GetLockPlanAsync(tx, Copy(frozen), visibleOriginal, ct);
        await tx.AcquireAsync(plan, ct);
        var binding = await _owner.ValidateContractAsync(tx, Copy(frozen), ct);
        ValidateBinding(binding);
        await tx.CheckAsync(ct);
        var input = new InboxInputV1(frozen, binding);
        var original = await FindTransport(db, frozen, ct);
        if (before?.Id != original?.Id || before?.RowVersion != original?.RowVersion)
            throw new InboxProtocolExceptionV1("CP6_INBOX_LOCK_ROOT_CHANGED_RETRY_ORIGINAL");
        var authority = await _owner.ReadAuthorityAsync(tx, input, ct);
        await tx.CheckAsync(ct);
        var decision = InboxContentDecisionV1.Evaluate(input, original, authority);
        if (decision == PreCheckpointIdentityDecisionV1.DependencyUnavailable)
            return new(Guid.Empty, binding.OperationKey, decision);

        var conflict = decision is PreCheckpointIdentityDecisionV1.TransportIdentityConflict or PreCheckpointIdentityDecisionV1.SameVersionConflict;
        var result = await FindOriginal(db, tx.TenantId, binding.Owner, binding.OperationKey, binding.RequestVersion, ct);
        if (!conflict && result is not null)
        {
            MatchInput(result.InputDomain, result.InputCodec, result.InputDigest, binding.InputDomain, binding.InputCodec, binding.InputDigest);
            if (decision == PreCheckpointIdentityDecisionV1.FreshCandidate)
                throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_OPERATION_ALREADY_EXISTS");
        }
        var checkpoint = await db.Set<InboxCheckpointV1>().SingleOrDefaultAsync(x => x.Consumer == frozen.ConsumerName &&
            x.TenantId == tx.TenantId && x.Aggregate == frozen.AggregateId, ct);
        if (decision == PreCheckpointIdentityDecisionV1.FreshCandidate && checkpoint is not null && checkpoint.Version >= frozen.AggregateVersion)
            return new(Guid.Empty, binding.OperationKey, PreCheckpointIdentityDecisionV1.DependencyUnavailable);

        // A global transport key collision must not let incoming tenant/aggregate choose a quarantine
        // target. Preserve a technical conflict only; the foreign Owner must investigate under its own authority.
        var applied = original is not null && original.TenantId != tx.TenantId
            ? new InboxOwnerResultV1("CP6_INBOX_FOREIGN_TRANSPORT_COLLISION", ReadOnlyMemory<byte>.Empty)
            : await _owner.ApplyDecisionAsync(tx, input, decision, original, ct);
        await tx.CheckAsync(ct);
        InboxValidationV1.Text(applied.NativeReceiptReference);
        var resultBytes = applied.OriginalResult.ToArray();
        if (conflict)
        {
            if (resultBytes.Length != 0) throw new InboxProtocolExceptionV1("CP6_INBOX_CONFLICT_CANNOT_CREATE_RESULT");
            result = null;
        }
        else if (result is null)
        {
            result = new InboxOriginalResultV1
            {
                Id = Guid.NewGuid(), TenantId = tx.TenantId, Owner = binding.Owner, OperationKey = binding.OperationKey,
                RequestVersion = binding.RequestVersion, InputDomain = binding.InputDomain, InputCodec = binding.InputCodec,
                InputDigest = binding.InputDigest, NativeReceiptReference = applied.NativeReceiptReference,
                Result = resultBytes, ResultSha256 = InboxInputV1.Hash(resultBytes)
            };
            db.Add(result);
        }
        else if (result.NativeReceiptReference != applied.NativeReceiptReference || !result.Result.AsSpan().SequenceEqual(resultBytes) ||
                 result.ResultSha256 != InboxInputV1.Hash(resultBytes))
            throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_RESULT_CHANGED");

        if (original is null && !conflict) db.Add(InboxTransportV1.From(input));
        if (decision == PreCheckpointIdentityDecisionV1.FreshCandidate)
        {
            if (checkpoint is null)
            {
                checkpoint = new InboxCheckpointV1 { Id = Guid.NewGuid(), TenantId = tx.TenantId,
                    Consumer = frozen.ConsumerName, Aggregate = frozen.AggregateId };
                db.Add(checkpoint);
            }
            checkpoint.Version = frozen.AggregateVersion;
        }
        var outcome = new InboxDeliveryOutcomeV1
        {
            Id = Guid.NewGuid(), TenantId = tx.TenantId, Owner = binding.Owner, OperationKey = binding.OperationKey,
            RequestVersion = binding.RequestVersion, InputDomain = binding.InputDomain, InputCodec = binding.InputCodec,
            InputDigest = binding.InputDigest, Consumer = frozen.ConsumerName, MessageId = frozen.MessageId,
            RawSha256 = input.RawEnvelopeSha256, DataSha256 = input.DataPayloadSha256, Decision = decision,
            NativeEvidenceReference = applied.NativeReceiptReference, OriginalResultId = result?.Id,
            RegistrationReference = binding.RegistrationReference,
            SourceReference = authority.Current?.SourceReference ?? "", SourceVersion = authority.Current?.Version
        };
        db.Add(outcome);
        db.ChangeTracker.DetectChanges();
        if (db.ChangeTracker.Entries().Any(e => e.Entity is InboxOriginalResultV1 or InboxTransportV1 or InboxDeliveryOutcomeV1 &&
            e.State is EntityState.Modified or EntityState.Deleted))
            throw new InboxProtocolExceptionV1("CP6_INBOX_IMMUTABLE_EVIDENCE_CHANGED");
        await db.SaveChangesAsync(ct);
        await tx.CheckAsync(ct);
        return new(outcome.Id, binding.OperationKey, decision);
    }

    /// <summary>
    /// Caller supplies a separate READ ONLY / ReadCommitted transaction. Missing receipt means unknown,
    /// not rollback. Query the original key/id/digest; never generate a replacement operation.
    /// </summary>
    public async Task<CommittedDeliveryOutcome?> ReadOriginalResultAsync(DbContext db, DbTransaction transaction,
        string owner, string operationKey, string requestVersion, string inputDomain, string inputCodec,
        string inputDigest, Guid receiptId, IInboxResultReaderV1 reader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var tx = await InboxTransactionV1.BindAsync(db, transaction, _context, true, ct);
        var outcome = await db.Set<InboxDeliveryOutcomeV1>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == receiptId &&
            x.TenantId == tx.TenantId && x.Owner == owner && x.OperationKey == operationKey && x.RequestVersion == requestVersion, ct);
        if (outcome is null) return null;
        MatchInput(outcome.InputDomain, outcome.InputCodec, outcome.InputDigest, inputDomain, inputCodec, inputDigest);
        var original = outcome.OriginalResultId is null ? null : await db.Set<InboxOriginalResultV1>().AsNoTracking()
            .SingleAsync(x => x.Id == outcome.OriginalResultId && x.TenantId == tx.TenantId, ct);
        if (original is not null && (original.Owner != owner || original.OperationKey != operationKey || original.RequestVersion != requestVersion ||
            original.ResultSha256 != InboxInputV1.Hash(original.Result)))
            throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_RESULT_CHANGED");
        var visible = await reader.AuthorizeAndFilterAsync(tx, outcome, original, ct);
        await tx.CheckAsync(ct);
        return new(outcome, original, visible);
    }

    private static Task<InboxTransportV1?> FindTransport(DbContext db, Cp6InboxDelivery d, CancellationToken ct) =>
        db.Set<InboxTransportV1>().AsNoTracking().SingleOrDefaultAsync(x => x.Consumer == d.ConsumerName && x.MessageId == d.MessageId, ct);

    /// <summary>Recover by the original Owner key even when no pending receipt id reached the caller. Read policy only.</summary>
    public async Task<CommittedOriginalResultV1?> ReadOriginalOperationAsync(DbContext db, DbTransaction transaction,
        string owner, string operationKey, string requestVersion, string inputDomain, string inputCodec,
        string inputDigest, IInboxResultReaderV1 reader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var tx = await InboxTransactionV1.BindAsync(db, transaction, _context, true, ct);
        var original = await FindOriginal(db, tx.TenantId, owner, operationKey, requestVersion, ct);
        if (original is null) return null;
        MatchInput(original.InputDomain, original.InputCodec, original.InputDigest, inputDomain, inputCodec, inputDigest);
        if (original.ResultSha256 != InboxInputV1.Hash(original.Result))
            throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_RESULT_CHANGED");
        var outcome = await db.Set<InboxDeliveryOutcomeV1>().AsNoTracking().Where(x => x.OriginalResultId == original.Id &&
            x.TenantId == tx.TenantId && x.Owner == owner && x.OperationKey == operationKey && x.RequestVersion == requestVersion)
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_EVIDENCE_MISSING");
        var visible = await reader.AuthorizeAndFilterAsync(tx, outcome, original, ct);
        await tx.CheckAsync(ct);
        return new(original, visible);
    }
    private static Task<InboxOriginalResultV1?> FindOriginal(DbContext db, Guid tenant, string owner, string key, string version, CancellationToken ct) =>
        db.Set<InboxOriginalResultV1>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.Owner == owner &&
            x.OperationKey == key && x.RequestVersion == version, ct);
    private static Cp6InboxDelivery Copy(Cp6InboxDelivery d) => d with { Payload = d.Payload.ToArray() };
    private static void MatchInput(string oldDomain, string oldCodec, string oldDigest, string domain, string codec, string digest)
    {
        if (oldDomain != domain || oldCodec != codec || oldDigest != digest)
            throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_INPUT_CONFLICT");
    }
    private static void ValidateDelivery(Cp6InboxDelivery d, Guid tenant)
    {
        if (d.TenantId != tenant) throw new InboxProtocolExceptionV1("CP6_INBOX_TENANT_BINDING_REJECTED");
        Cp6TransactionalMessagingGuard.Delivery(d);
    }
    private static void ValidateBinding(InboxContractBindingV1 b)
    {
        ArgumentNullException.ThrowIfNull(b);
        foreach (var value in new[] { b.Owner, b.OperationKey, b.RequestVersion, b.InputDomain, b.InputCodec,
            b.EventType, b.Source, b.Region, b.SchemaIdentity, b.BundleIdentity, b.RegistrationReference }) InboxValidationV1.Text(value);
        if (!InboxValidationV1.IsHash(b.InputDigest) || b.InputDomain is "rawEnvelopeSha256" or "dataPayloadSha256")
            throw new InboxProtocolExceptionV1("CP6_INBOX_INPUT_DIGEST_DOMAIN_INVALID");
    }
}
