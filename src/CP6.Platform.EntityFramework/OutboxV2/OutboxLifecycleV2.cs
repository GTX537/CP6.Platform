using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework.OutboxV2;

public sealed partial class CallerTransactionOutboxV2
{
    public async Task<OutboxSwitchResultV2> BeginDrainV2(OutboxTransactionV2 tx, Guid switchId, long expectedEpoch,
        string expectedControlVersion, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(switchId); await tx.Check(OutboxAccessV2.Switch, ct);
        var tuple = OutboxEncodingV2.Json(new { Kind = "BeginDrain", switchId, expectedEpoch, expectedControlVersion });
        var prior = await SwitchReplay(tx, switchId, tuple, ct); if (prior is not null) return prior;
        var root = await Root(tx, ct);
        if (!Available(tx, root)) return new(OutboxDecisionV2.RouteUnavailable, root?.CurrentEpoch ?? 0);
        if (root!.CurrentEpoch != expectedEpoch) return new(OutboxDecisionV2.RouteEpochChanged, root.CurrentEpoch);
        if (OutboxEncodingV2.Encode(root.Version) != expectedControlVersion) return new(OutboxDecisionV2.Conflict, root.CurrentEpoch);
        await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxRouteV2")} SET \"Phase\"=@phase, \"LastSwitchId\"=@id WHERE \"Id\"=@root", ct,
            ("phase", (int)OutboxPhaseV2.Draining), ("id", switchId), ("root", root.Id));
        return await SaveSwitch(tx, switchId, tuple, "Draining", ct);
    }
    public async Task<OutboxSwitchResultV2> AdvanceEpochV2(OutboxTransactionV2 tx, Guid mutationId, Guid switchId,
        long expectedEpoch, string expectedControlVersion, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(mutationId); OutboxEncodingV2.Key(switchId); await tx.Check(OutboxAccessV2.Switch, ct);
        var tuple = OutboxEncodingV2.Json(new { Kind = "AdvanceEpoch", mutationId, switchId, expectedEpoch, expectedControlVersion });
        var prior = await SwitchReplay(tx, mutationId, tuple, ct); if (prior is not null) return prior;
        var root = await Root(tx, ct);
        if (!Matches(tx, root) || root!.LastSwitchId != switchId ||
            root.Phase is not (OutboxPhaseV2.Draining or OutboxPhaseV2.Blocked) || root.LockKey != tx.Registration.RouteLockKey ||
            root.PublisherSetReference != tx.Registration.PublisherSetReference)
            return new(OutboxDecisionV2.RouteUnavailable, root?.CurrentEpoch ?? 0);
        var drain = await tx.Context.Set<OutboxLeaseMutationReceiptV2>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == root.Id && x.MutationId == switchId, ct);
        if (drain is null || drain.WriterTransaction == tx.TransactionId)
            return new(OutboxDecisionV2.RouteUnavailable, root.CurrentEpoch);
        if (root.CurrentEpoch != expectedEpoch) return new(OutboxDecisionV2.RouteEpochChanged, root.CurrentEpoch);
        if (OutboxEncodingV2.Encode(root.Version) != expectedControlVersion || root.CurrentEpoch == long.MaxValue) return new(OutboxDecisionV2.Conflict, root.CurrentEpoch);
        var rows = await (from n in tx.Context.Set<Cp6OutboxMessage>().AsNoTracking()
            join b in tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking() on n.Id equals b.NativeId
            where b.RootId == root.Id && n.Status != Cp6OutboxStatus.Published && n.Status != Cp6OutboxStatus.DeadLettered
            select n.Id).Take(tx.Registration.Policy.BatchLimit + 1).ToArrayAsync(ct);
        if (rows.Length > tx.Registration.Policy.BatchLimit) return new(OutboxDecisionV2.RouteUnavailable, root.CurrentEpoch);
        await OutboxPostgreSqlV2.LockNative(tx, rows, ct);
        var handover = await owner.VerifyHandoverAsync(tx, switchId, ct);
        if (handover is null || !handover.UnknownTransactionsAuthoritativelyResolved ||
            !handover.NonterminalIds.Order().SequenceEqual(rows.Order()) || string.IsNullOrWhiteSpace(handover.Reference) || string.IsNullOrWhiteSpace(handover.PublisherSetReference))
        {
            await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxRouteV2")} SET \"Phase\"=@phase WHERE \"Id\"=@root", ct,
                ("phase", (int)OutboxPhaseV2.Blocked), ("root", root.Id));
            Audit(tx, null, "EpochBlocked", switchId.ToString("D"), await Now(tx, ct)); await tx.Context.SaveChangesAsync(ct);
            return new(OutboxDecisionV2.PendingCommit, root.CurrentEpoch, OutboxEncodingV2.Encode((await Root(tx, ct))!.Version));
        }
        var now = await Now(tx, ct);
        foreach (var id in rows)
        {
            tx.EnsureUnsealed(id);
            var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var b = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleAsync(x => x.NativeId == id, ct);
            if (n.TenantId != tx.TenantId || b.AssignedEpoch != root.CurrentEpoch || b.MessageId != n.MessageId ||
                b.RawEnvelopeSha256 != n.PayloadSha256 || OutboxEncodingV2.Hash(n.Payload) != b.RawEnvelopeSha256 ||
                (n.Status == Cp6OutboxStatus.Dispatching && (n.LeaseExpiresAtUtc is null || b.ClaimEpoch != root.CurrentEpoch || b.CurrentClaimId is null)))
                return new(OutboxDecisionV2.Conflict, root.CurrentEpoch);
            if (n.Status == Cp6OutboxStatus.Dispatching && n.LeaseExpiresAtUtc > now) return new(OutboxDecisionV2.Retained, root.CurrentEpoch);
        }
        await tx.Permit(ct);
        foreach (var id in rows)
        {
            var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            if (n.Status == Cp6OutboxStatus.Dispatching)
            {
                await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxMessage")} SET \"Status\"=0, \"LeaseOwner\"=NULL, \"LeaseToken\"=NULL, \"LeaseExpiresAtUtc\"=NULL WHERE \"Id\"=@id", ct, ("id", id));
                Audit(tx, id, "UNCERTAIN_DELIVERY", handover.Reference, now);
            }
            await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxBindingV2")} SET \"AssignedEpoch\"=@epoch, \"ClaimEpoch\"=NULL, \"CurrentClaimId\"=NULL WHERE \"NativeId\"=@id", ct,
                ("epoch", root.CurrentEpoch + 1), ("id", id));
            Audit(tx, id, "EpochHandover", handover.Reference, now);
        }
        await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxRouteV2")} SET \"CurrentEpoch\"=@epoch, \"Phase\"=0, \"PublisherSetReference\"=@publishers WHERE \"Id\"=@root", ct,
            ("epoch", root.CurrentEpoch + 1), ("publishers", handover.PublisherSetReference), ("root", root.Id));
        return await SaveSwitch(tx, mutationId, tuple, "EpochAdvanced", ct);
    }
    private static async Task<OutboxSwitchResultV2?> SwitchReplay(OutboxTransactionV2 tx, Guid id, string tuple, CancellationToken ct)
    {
        var r = await tx.Context.Set<OutboxLeaseMutationReceiptV2>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == tx.Registration.Root.Id && x.MutationId == id, ct);
        if (r is null) return null;
        if (r.Input != tuple) return new(OutboxDecisionV2.Conflict, 0);
        return JsonSerializer.Deserialize<OutboxSwitchResultV2>(r.Output)! with { Decision = OutboxDecisionV2.PendingCommit };
    }
    private static async Task<OutboxSwitchResultV2> SaveSwitch(OutboxTransactionV2 tx, Guid id, string tuple, string transition, CancellationToken ct)
    {
        var root = (await Root(tx, ct))!;
        var result = new OutboxSwitchResultV2(OutboxDecisionV2.PendingCommit, root.CurrentEpoch, OutboxEncodingV2.Encode(root.Version));
        tx.Context.Add(new OutboxLeaseMutationReceiptV2 { RootId = root.Id, MutationId = id, NativeId = Guid.Empty, Input = tuple, Output = OutboxEncodingV2.Json(result), WriterTransaction = tx.TransactionId });
        Audit(tx, null, transition, id.ToString("D"), await Now(tx, ct)); await tx.Context.SaveChangesAsync(ct); return result;
    }

    public async Task<OutboxRetentionResultV1> DeleteEligibleInTransactionV1(OutboxTransactionV2 tx,
        IReadOnlyList<OutboxRetentionCandidateV1> candidates, CancellationToken ct = default)
    {
        await tx.Check(OutboxAccessV2.Retention, ct);
        if (candidates.Count > tx.Registration.Policy.BatchLimit || candidates.Select(x => x.NativeId).Distinct().Count() != candidates.Count)
            throw new OutboxProtocolExceptionV2("CP6_OUTBOX_RETENTION_BATCH_INVALID");
        var root = await Root(tx, ct);
        if (!Available(tx, root, true)) return new(OutboxDecisionV2.RouteUnavailable, [], candidates.Select(x => x.NativeId).ToArray());
        await OutboxPostgreSqlV2.LockNative(tx, candidates.Select(x => x.NativeId), ct);
        var deleted = new List<Guid>(); var retained = new List<Guid>(); var noPolicy = false;
        foreach (var candidate in candidates.OrderBy(x => x.NativeId))
        {
            var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == candidate.NativeId, ct);
            var b = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleOrDefaultAsync(x => x.NativeId == candidate.NativeId, ct);
            if (n is null || b is null || b.RootId != root!.Id || n.TenantId != tx.TenantId || NativeVersion(n) != candidate.ExpectedNativeVersion ||
                n.Status is not (Cp6OutboxStatus.Published or Cp6OutboxStatus.DeadLettered) || n.LeaseToken is not null || b.CurrentClaimId is not null ||
                n.MessageId != b.MessageId || n.PayloadSha256 != b.RawEnvelopeSha256 || OutboxEncodingV2.Hash(n.Payload) != b.RawEnvelopeSha256)
            { retained.Add(candidate.NativeId); continue; }
            var e = await owner.RetentionEligibilityAsync(tx, candidate, ct);
            if (e is null || e.PolicyId != candidate.PolicyId || e.PolicyVersion != candidate.PolicyVersion)
            { noPolicy = true; retained.Add(candidate.NativeId); continue; }
            if (string.IsNullOrWhiteSpace(e.TerminalOwnerReceipt) || string.IsNullOrWhiteSpace(e.RequiredConsumerSetVersion) ||
                !e.AllConsumersComplete || !e.ReferenceClosureComplete || e.HasHold || e.HasUnknownOrConflict || e.RetainUntilUtc > await Now(tx, ct) ||
                string.IsNullOrWhiteSpace(e.DedupProtectionReference) || string.IsNullOrWhiteSpace(e.ArchiveReference) || !e.ArchiveVerified)
            { retained.Add(candidate.NativeId); continue; }
            // Receipts/audits/seals survive: absence of native payload can never mean NOT_APPLIED.
            tx.EnsureUnsealed(n.Id); await tx.Permit(ct);
            await OutboxPostgreSqlV2.Execute(tx.Context, $"DELETE FROM {tx.Table("Cp6_DeadLetterRecord")} WHERE \"MessageId\"=@message AND \"TenantId\"=@tenant AND \"Direction\"=0", ct,
                ("message", n.MessageId), ("tenant", n.TenantId));
            await OutboxPostgreSqlV2.Execute(tx.Context, $"DELETE FROM {tx.Table("Cp6_OutboxBindingV2")} WHERE \"NativeId\"=@id", ct, ("id", n.Id));
            await OutboxPostgreSqlV2.Execute(tx.Context, $"DELETE FROM {tx.Table("Cp6_OutboxMessage")} WHERE \"Id\"=@id", ct, ("id", n.Id));
            Audit(tx, n.Id, "RetentionDeleted", OutboxEncodingV2.Json(new { Candidate = candidate, Evidence = e, n.MessageId, n.PayloadSha256, MissingPayloadMeaning = "EVIDENCE_EXPIRED" }), await Now(tx, ct));
            deleted.Add(n.Id);
        }
        await tx.Context.SaveChangesAsync(ct);
        return new(deleted.Count > 0 ? OutboxDecisionV2.PendingCommit : noPolicy ? OutboxDecisionV2.PolicyUnavailable : OutboxDecisionV2.Retained, deleted.AsReadOnly(), retained.AsReadOnly());
    }
}
