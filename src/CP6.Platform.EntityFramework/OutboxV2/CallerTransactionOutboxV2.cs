using System.Data.Common;
using System.Text.Json;
using CP6.Platform.Abstractions.Context;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework.OutboxV2;

public sealed partial class CallerTransactionOutboxV2(ITrustedContextAccessorV1 trusted, IOutboxOwnerV2 owner)
{
    public Task<OutboxTransactionV2> BindTransactionAsync(DbContext db, DbTransaction transaction, OutboxRootKeyV2 root,
        bool readOnly = false, CancellationToken ct = default) => OutboxTransactionV2.Bind(db, transaction, trusted, owner, root, readOnly, ct);

    private static Task<OutboxRouteControlV2?> Root(OutboxTransactionV2 tx, CancellationToken ct) =>
        tx.Context.Set<OutboxRouteControlV2>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == tx.Registration.Root.Id, ct);
    private static bool Matches(OutboxTransactionV2 tx, OutboxRouteControlV2? root) =>
        root is not null && root.Id == tx.Registration.Root.Id && root.OwnerId == tx.Registration.Root.OwnerId &&
        root.TenantId == tx.TenantId && root.QueueDomainId == tx.Registration.Root.QueueDomainId && root.RouteId == tx.Registration.Root.RouteId &&
        root.LockKey == tx.Registration.RouteLockKey && root.ProfileId == OutboxEncodingV2.Profile && root.ProfileVersion == 1 && root.CurrentEpoch > 0 &&
        root.PublisherSetReference == tx.Registration.PublisherSetReference;
    private static bool Available(OutboxTransactionV2 tx, OutboxRouteControlV2? root, bool draining = false) =>
        Matches(tx, root) && (root!.Phase == OutboxPhaseV2.Active || (draining && root.Phase == OutboxPhaseV2.Draining));
    private static DateTimeOffset Utc(object value) => value is DateTimeOffset o ? o : new DateTimeOffset(DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc));
    private static async Task<DateTimeOffset> Now(OutboxTransactionV2 tx, CancellationToken ct) => Utc((await OutboxPostgreSqlV2.Scalar(tx.Context, "SELECT clock_timestamp()", ct))!);
    private static string NativeVersion(Cp6OutboxMessage n) => OutboxEncodingV2.Encode(OutboxEncodingV2.NativeValue(n.RowVersion));
    internal static bool LeaseIsLive(DateTimeOffset databaseFenceAtUtc, DateTimeOffset expiry) => databaseFenceAtUtc < expiry;
    private static void Audit(OutboxTransactionV2 tx, Guid? id, string kind, string evidence, DateTimeOffset at) =>
        tx.Context.Add(new OutboxAuditV2 { Id = Guid.NewGuid(), RootId = tx.Registration.Root.Id, NativeId = id, Kind = kind, Evidence = evidence, AtUtc = at });

    public async Task<OutboxClaimResultV2> ClaimBatchV2(OutboxTransactionV2 tx, Guid claimRequestId, int batchSize, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(claimRequestId);
        if (batchSize < 1 || batchSize > tx.Registration.Policy.BatchLimit) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_BATCH_INVALID");
        await tx.Check(OutboxAccessV2.ReadReceipt, ct);
        var input = OutboxEncodingV2.Json(new { Root = tx.Registration.Root, claimRequestId, batchSize, tx.Registration.WorkerId, tx.Registration.Policy, tx.Registration.PublisherSetReference });
        var old = await tx.Context.Set<OutboxClaimReceiptV2>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == tx.Registration.Root.Id && x.RequestId == claimRequestId, ct);
        if (old is not null) return old.Input == input
            ? new(OutboxDecisionV2.PendingCommit, new(claimRequestId, input, JsonSerializer.Deserialize<ClaimTicketV2[]>(old.Tickets)!))
            : new(OutboxDecisionV2.Conflict);
        await tx.Check(OutboxAccessV2.Claim, ct);
        var root = await Root(tx, ct);
        if (!Available(tx, root)) return new(OutboxDecisionV2.RouteUnavailable);
        var now = await Now(tx, ct);
        var rows = await (from n in tx.Context.Set<Cp6OutboxMessage>().AsNoTracking()
            join b in tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking() on n.Id equals b.NativeId
            where b.RootId == root!.Id && b.AssignedEpoch == root.CurrentEpoch && n.TenantId == tx.TenantId &&
                ((n.Status == Cp6OutboxStatus.Pending && n.AvailableAtUtc <= now) ||
                 (n.Status == Cp6OutboxStatus.Dispatching && n.LeaseExpiresAtUtc <= now))
            orderby n.AvailableAtUtc, n.CreatedAtUtc, n.Id
            select n.Id).Take(batchSize).ToArrayAsync(ct);
        await OutboxPostgreSqlV2.LockNative(tx, rows, ct);
        var tickets = new List<ClaimTicketV2>();
        // Complete all validation before the first state write; a rejection has a genuinely empty write set.
        foreach (var id in rows)
        {
            var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var b = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleAsync(x => x.NativeId == id, ct);
            tx.EnsureUnsealed(id);
            if (n.MessageId != b.MessageId || n.PayloadSha256 != b.RawEnvelopeSha256 || OutboxEncodingV2.Hash(n.Payload) != b.RawEnvelopeSha256 ||
                n.TenantId != tx.TenantId || b.RootId != root!.Id || b.AssignedEpoch != root.CurrentEpoch ||
                (n.Status == Cp6OutboxStatus.Dispatching && (b.CurrentClaimId is null || b.ClaimEpoch != root.CurrentEpoch)))
                return new(OutboxDecisionV2.Conflict);
        }
        await tx.Permit(ct);
        foreach (var id in rows)
        {
            var claimId = Guid.NewGuid(); var token = Guid.NewGuid().ToString("N"); var receipt = Guid.NewGuid();
            var oldNative = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var at = await Now(tx, ct); var expiry = at.Add(tx.Registration.Policy.LeaseDuration);
            if (oldNative.Status == Cp6OutboxStatus.Dispatching) Audit(tx, id, "UNCERTAIN_DELIVERY", "expired-original-claim", at);
            await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxMessage")} SET \"Status\"=@status, \"LeaseOwner\"=@worker, \"LeaseToken\"=@token, \"LeaseExpiresAtUtc\"=@expiry, \"AttemptCount\"=\"AttemptCount\"+1 WHERE \"Id\"=@id", ct,
                ("status", (int)Cp6OutboxStatus.Dispatching), ("worker", tx.Registration.WorkerId), ("token", token), ("expiry", expiry), ("id", id));
            await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxBindingV2")} SET \"CurrentClaimId\"=@claim, \"ClaimEpoch\"=@epoch, \"ClaimStartedAtUtc\"=@at WHERE \"NativeId\"=@id", ct,
                ("claim", claimId), ("epoch", root!.CurrentEpoch), ("at", at), ("id", id));
            var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var b = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleAsync(x => x.NativeId == id, ct);
            tickets.Add(new(OutboxEncodingV2.Profile, 1, tx.Registration.Root, id, n.MessageId, b.RawEnvelopeSha256, b.AssignedEpoch, b.ClaimEpoch!.Value,
                claimId, n.LeaseOwner!, n.LeaseToken!, n.LeaseExpiresAtUtc!.Value, at, NativeVersion(n), OutboxEncodingV2.Encode(b.Version),
                "ClaimCommit", receipt, claimRequestId, receipt, OutboxEncodingV2.Json(n.ToDispatchMessage())));
            Audit(tx, id, "ClaimPrepared", receipt.ToString("D"), at);
        }
        tx.Context.Add(new OutboxClaimReceiptV2 { RootId = root!.Id, RequestId = claimRequestId, Input = input, Tickets = OutboxEncodingV2.Json(tickets), WriterTransaction = tx.TransactionId });
        foreach (var ticket in tickets) tx.Context.Add(new OutboxClaimTicketReceiptV2 { RootId = root.Id, RequestId = claimRequestId, NativeId = ticket.NativeId,
            ReceiptReference = ticket.ClaimReceiptRef, Ticket = OutboxEncodingV2.Json(ticket), WriterTransaction = tx.TransactionId });
        await tx.Context.SaveChangesAsync(ct);
        foreach (var id in rows) await tx.Seal(id, ct);
        return new(OutboxDecisionV2.PendingCommit, new(claimRequestId, input, tickets));
    }

    public async Task<OutboxClaimResultV2> QueryClaimV2(OutboxTransactionV2 tx, Guid requestId, string exactInput, CancellationToken ct = default)
    {
        await tx.Check(OutboxAccessV2.ReadReceipt, ct);
        if (!tx.ReadOnly) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_INDEPENDENT_READ_REQUIRED");
        var receipt = await tx.Context.Set<OutboxClaimReceiptV2>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == tx.Registration.Root.Id && x.RequestId == requestId, ct);
        if (receipt is null || receipt.WriterTransaction == tx.TransactionId) return new(OutboxDecisionV2.OutcomeUnknown);
        if (receipt.Input != exactInput) return new(OutboxDecisionV2.Conflict);
        return new(OutboxDecisionV2.Committed, Committed: Array.AsReadOnly(JsonSerializer.Deserialize<ClaimTicketV2[]>(receipt.Tickets)!.Select(x => new CommittedClaimTicketV2(x)).ToArray()));
    }

    private static async Task<(OutboxDecisionV2? Rejected, Cp6OutboxMessage? Native)> Gate(OutboxTransactionV2 tx, ClaimTicketV2 t, bool renewal, CancellationToken ct)
    {
        if (t.Root != tx.Registration.Root || t.ProfileId != OutboxEncodingV2.Profile || t.ProfileVersion != 1 || t.LeaseOwner != tx.Registration.WorkerId)
            return (OutboxDecisionV2.Conflict, null);
        var root = await Root(tx, ct);
        if (!Available(tx, root, !renewal)) return (OutboxDecisionV2.RouteUnavailable, null);
        await OutboxPostgreSqlV2.LockNative(tx, [t.NativeId], ct);
        var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == t.NativeId, ct);
        var b = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleOrDefaultAsync(x => x.NativeId == t.NativeId, ct);
        if (n is null || b is null) return (OutboxDecisionV2.LeaseLost, null);
        if (root!.CurrentEpoch != t.AssignedEpoch || root.CurrentEpoch != t.ClaimEpoch || b.AssignedEpoch != t.AssignedEpoch || b.ClaimEpoch != t.ClaimEpoch)
            return (OutboxDecisionV2.RouteEpochChanged, null);
        if (b.RootId != root.Id || n.TenantId != t.Root.TenantId || n.MessageId != t.MessageId || b.MessageId != t.MessageId ||
            b.RawEnvelopeSha256 != t.RawEnvelopeSha256 || n.PayloadSha256 != t.RawEnvelopeSha256 || OutboxEncodingV2.Hash(n.Payload) != t.RawEnvelopeSha256)
            return (OutboxDecisionV2.Conflict, null);
        if (n.Status != Cp6OutboxStatus.Dispatching || n.LeaseOwner != t.LeaseOwner || n.LeaseToken != t.LeaseToken ||
            n.LeaseExpiresAtUtc != t.LeaseExpiresAtUtc || b.CurrentClaimId != t.ClaimId || b.ClaimStartedAtUtc != t.ClaimStartedAtUtc)
            return (OutboxDecisionV2.LeaseLost, null);
        if (NativeVersion(n) != t.ExpectedNativeVersion || OutboxEncodingV2.Encode(b.Version) != t.ExpectedBindingVersion ||
            OutboxEncodingV2.Json(n.ToDispatchMessage()) != t.DispatchJson) return (OutboxDecisionV2.Conflict, null);
        if (!LeaseIsLive(await Now(tx, ct), n.LeaseExpiresAtUtc!.Value)) return (OutboxDecisionV2.Expired, null);
        return (null, n);
    }
    private static async Task<OutboxMutationResultV2?> Replay(OutboxTransactionV2 tx, Guid mutationId, string input, CancellationToken ct)
    {
        await tx.Check(OutboxAccessV2.ReadReceipt, ct);
        var old = await tx.Context.Set<OutboxLeaseMutationReceiptV2>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == tx.Registration.Root.Id && x.MutationId == mutationId, ct);
        if (old is null) return null;
        if (old.Input != input) return new(OutboxDecisionV2.Conflict, mutationId);
        var result = JsonSerializer.Deserialize<OutboxMutationResultV2>(old.Output)!;
        return result with { Decision = tx.ReadOnly && old.WriterTransaction != tx.TransactionId ? OutboxDecisionV2.Committed : OutboxDecisionV2.PendingCommit };
    }
    public async Task<OutboxMutationResultV2> QueryClaimMutationV2(OutboxTransactionV2 tx, Guid mutationId, string exactInput, CancellationToken ct = default)
    {
        if (!tx.ReadOnly) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_INDEPENDENT_READ_REQUIRED");
        return await Replay(tx, mutationId, exactInput, ct) ?? new(OutboxDecisionV2.OutcomeUnknown, mutationId);
    }
    public static string MutationInput(OutboxMutationV2 input) => OutboxEncodingV2.Json(new { Kind = "Complete", Input = input });
    public static string MutationInput(OutboxRenewalV2 input) => OutboxEncodingV2.Json(new { Kind = "Renew", Input = input });

    public async Task<OutboxMutationResultV2> CompleteClaimV2(OutboxTransactionV2 tx, CommittedClaimTicketV2 committed, OutboxMutationV2 input, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(input.MutationId);
        if (committed.Ticket != input.Ticket) return new(OutboxDecisionV2.Conflict, input.MutationId);
        var tuple = MutationInput(input); var replay = await Replay(tx, input.MutationId, tuple, ct); if (replay is not null) return replay;
        await tx.Check(OutboxAccessV2.Complete, ct);
        if (input.PolicyVersion != tx.Registration.Policy.Version || !Enum.IsDefined(input.Intent)) return new(OutboxDecisionV2.Conflict, input.MutationId);
        if (input.Intent == OutboxIntentV2.RecordPublishFailure) Cp6TransactionalMessagingGuard.ContentSafeCode(input.SafeErrorCode!, nameof(input.SafeErrorCode));
        else if (input.SafeErrorCode is not null || input.Retryable || input.SupportReference is not null) return new(OutboxDecisionV2.Conflict, input.MutationId);
        if (input.SupportReference is not null) Cp6TransactionalMessagingGuard.Identifier(input.SupportReference, nameof(input.SupportReference), 128);
        var (reject, n) = await Gate(tx, input.Ticket, false, ct); if (reject is not null) return new(reject.Value, input.MutationId);
        tx.EnsureUnsealed(n!.Id); await tx.Permit(ct);
        var status = input.Intent == OutboxIntentV2.PublishConfirmed ? Cp6OutboxStatus.Published :
            input.Retryable && n.AttemptCount < tx.Registration.Policy.MaxAttempts ? Cp6OutboxStatus.Pending : Cp6OutboxStatus.DeadLettered;
        var transition = status == Cp6OutboxStatus.Published ? "Published" : status == Cp6OutboxStatus.Pending ? "RetryScheduled" : "DeadLettered";
        var delay = Math.Min(tx.Registration.Policy.MaximumRetryDelay.TotalSeconds,
            tx.Registration.Policy.RetryDelay.TotalSeconds * Math.Pow(2, Math.Min(n.AttemptCount - 1, 30)));
        var atValue = await OutboxPostgreSqlV2.Scalar(tx.Context, $"WITH fence AS MATERIALIZED (SELECT clock_timestamp() AS at) UPDATE {tx.Table("Cp6_OutboxMessage")} n SET " +
            "\"Status\"=@status, \"PublishedAtUtc\"=CASE WHEN @status=2 THEN fence.at ELSE n.\"PublishedAtUtc\" END, " +
            "\"DeadLetteredAtUtc\"=CASE WHEN @status=3 THEN fence.at ELSE NULL END, \"AvailableAtUtc\"=CASE WHEN @status=0 THEN fence.at+(@delay * interval '1 second') ELSE n.\"AvailableAtUtc\" END, " +
            "\"LeaseOwner\"=NULL, \"LeaseToken\"=NULL, \"LeaseExpiresAtUtc\"=NULL, \"LastErrorCode\"=@error, \"SupportReference\"=@support " +
            "FROM fence WHERE n.\"Id\"=@id AND n.xmin::text=@rv AND fence.at<n.\"LeaseExpiresAtUtc\" RETURNING fence.at", ct,
            ("status", (int)status), ("delay", delay), ("error", input.SafeErrorCode), ("support", input.SupportReference), ("id", n.Id), ("rv", OutboxEncodingV2.NativeValue(n.RowVersion).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (atValue is null) return new(OutboxDecisionV2.Expired, input.MutationId);
        var at = Utc(atValue);
        await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxBindingV2")} SET \"CurrentClaimId\"=NULL, \"ClaimEpoch\"=NULL WHERE \"NativeId\"=@id", ct, ("id", n.Id));
        if (status == Cp6OutboxStatus.DeadLettered) tx.Context.Add(new Cp6DeadLetterRecord(Cp6DeadLetterDirection.Outbound, n.TenantId, n.MessageId, null, n.PayloadSha256, input.SafeErrorCode!, input.SupportReference, at));
        var result = new OutboxMutationResultV2(OutboxDecisionV2.PendingCommit, input.MutationId, transition, Guid.NewGuid());
        await SaveMutation(tx, n.Id, input.MutationId, tuple, result, at, ct); return result;
    }

    public async Task<OutboxMutationResultV2> RenewClaimV2(OutboxTransactionV2 tx, CommittedClaimTicketV2 committed, OutboxRenewalV2 input, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(input.MutationId);
        if (input.Ticket != committed.Ticket) return new(OutboxDecisionV2.Conflict, input.MutationId);
        var tuple = MutationInput(input); var replay = await Replay(tx, input.MutationId, tuple, ct); if (replay is not null) return replay;
        await tx.Check(OutboxAccessV2.Renew, ct);
        if (input.PolicyVersion != tx.Registration.Policy.Version || input.Duration <= TimeSpan.Zero || input.Duration > tx.Registration.Policy.LeaseDuration)
            return new(OutboxDecisionV2.Conflict, input.MutationId);
        var (reject, n) = await Gate(tx, input.Ticket, true, ct); if (reject is not null) return new(reject.Value, input.MutationId);
        tx.EnsureUnsealed(n!.Id); await tx.Permit(ct);
        var expiry = await OutboxPostgreSqlV2.Scalar(tx.Context, $"WITH fence AS MATERIALIZED (SELECT clock_timestamp() AS at) UPDATE {tx.Table("Cp6_OutboxMessage")} n " +
            "SET \"LeaseExpiresAtUtc\"=LEAST(fence.at+(@duration * interval '1 second'), @ceiling) FROM fence " +
            "WHERE n.\"Id\"=@id AND n.xmin::text=@rv AND fence.at<n.\"LeaseExpiresAtUtc\" AND LEAST(fence.at+(@duration * interval '1 second'), @ceiling)>n.\"LeaseExpiresAtUtc\" RETURNING n.\"LeaseExpiresAtUtc\"", ct,
            ("duration", input.Duration.TotalSeconds), ("ceiling", input.Ticket.ClaimStartedAtUtc.Add(tx.Registration.Policy.MaximumClaimLifetime)), ("id", n.Id), ("rv", OutboxEncodingV2.NativeValue(n.RowVersion).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (expiry is null) return new(OutboxDecisionV2.Expired, input.MutationId);
        await OutboxPostgreSqlV2.Execute(tx.Context, $"UPDATE {tx.Table("Cp6_OutboxBindingV2")} SET \"CurrentClaimId\"=\"CurrentClaimId\" WHERE \"NativeId\"=@id", ct, ("id", n.Id));
        var postNative = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == n.Id, ct);
        var postBinding = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleAsync(x => x.NativeId == n.Id, ct);
        var receiptRef = Guid.NewGuid();
        var renewed = input.Ticket with { LeaseExpiresAtUtc = Utc(expiry), ExpectedNativeVersion = NativeVersion(postNative), ExpectedBindingVersion = OutboxEncodingV2.Encode(postBinding.Version), VersionSource = "RenewCommit", VersionReceiptRef = receiptRef };
        var result = new OutboxMutationResultV2(OutboxDecisionV2.PendingCommit, input.MutationId, "Renewed", receiptRef, renewed);
        await SaveMutation(tx, n.Id, input.MutationId, tuple, result, await Now(tx, ct), ct); return result;
    }
    private static async Task SaveMutation(OutboxTransactionV2 tx, Guid nativeId, Guid mutationId, string tuple, OutboxMutationResultV2 result, DateTimeOffset at, CancellationToken ct)
    {
        tx.Context.Add(new OutboxLeaseMutationReceiptV2 { RootId = tx.Registration.Root.Id, MutationId = mutationId, NativeId = nativeId, Input = tuple, Output = OutboxEncodingV2.Json(result), WriterTransaction = tx.TransactionId });
        Audit(tx, nativeId, result.Transition!, result.ReceiptReference!.Value.ToString("D"), at);
        await tx.Context.SaveChangesAsync(ct); await tx.Seal(nativeId, ct);
    }
    public async Task<CommittedClaimTicketV2?> QueryRenewedTicketV2(OutboxTransactionV2 tx, OutboxRenewalV2 input, CancellationToken ct = default)
    {
        var result = await QueryClaimMutationV2(tx, input.MutationId, MutationInput(input), ct);
        return result.Decision == OutboxDecisionV2.Committed && result.RenewedTicket is not null ? new(result.RenewedTicket) : null;
    }
    public async Task<OutboxDecisionV2> ValidateSendV2(OutboxTransactionV2 tx, CommittedClaimTicketV2 committed, CancellationToken ct = default)
    {
        await tx.Check(OutboxAccessV2.Send, ct);
        var (reject, _) = await Gate(tx, committed.Ticket, true, ct);
        if (reject is not null) return reject.Value;
        return await Now(tx, ct) + tx.Registration.Policy.PublishBudget < committed.Ticket.LeaseExpiresAtUtc ? OutboxDecisionV2.Committed : OutboxDecisionV2.Expired;
    }

    public async Task<OutboxDecisionV2> PrepareSendV2(OutboxTransactionV2 tx, CommittedClaimTicketV2 committed, Guid mutationId, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(mutationId); await tx.Check(OutboxAccessV2.Send, ct);
        var original = await tx.Context.Set<OutboxSendAttemptV2>().AsNoTracking().SingleOrDefaultAsync(x => x.RootId == tx.Registration.Root.Id && x.ClaimId == committed.Ticket.ClaimId, ct);
        if (original is not null) return original.MutationId == mutationId && original.OriginalTicket == OutboxEncodingV2.Json(committed.Ticket)
            ? OutboxDecisionV2.OutcomeUnknown : OutboxDecisionV2.Conflict;
        var gate = await ValidateSendV2(tx, committed, ct); if (gate != OutboxDecisionV2.Committed) return gate;
        tx.Context.Add(new OutboxSendAttemptV2 { RootId = tx.Registration.Root.Id, ClaimId = committed.Ticket.ClaimId, MutationId = mutationId,
            OriginalTicket = OutboxEncodingV2.Json(committed.Ticket), PolicyVersion = tx.Registration.Policy.Version, WriterTransaction = tx.TransactionId });
        await tx.Context.SaveChangesAsync(ct); return OutboxDecisionV2.PendingCommit;
    }
}
