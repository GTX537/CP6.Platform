using System.Data;
using CP6.Platform.Abstractions.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Platform.EntityFramework.OutboxV2;

public enum OutboxTransportDecisionV2 { Confirmed, DefinitelyRejected, Unknown }
public sealed record OutboxTransportResultV2(OutboxTransportDecisionV2 Decision, string? SafeErrorCode = null,
    bool Retryable = false, string? SupportReference = null);
/// <summary>DefinitelyRejected requires transport-specific evidence of nonacceptance; exceptions are ambiguous.</summary>
public interface IOutboxPublisherV2
{
    ValueTask<OutboxTransportResultV2> PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken ct);
}
public sealed record OutboxDispatchObservationV2(string State, Guid ClaimRequestId, Guid? MutationId,
    OutboxMutationV2? OriginalMutation, OutboxDecisionV2? CompletionDecision, string? OriginalClaimInput = null);

/// <summary>Owns short technical transactions as a caller of the store. No network activity inside them.
/// Route selection is explicit V2: unready/rejected routes never fall back to the legacy dispatcher.</summary>
public sealed class OutboxDispatcherV2<TContext>(IDbContextFactory<TContext> factory, ITrustedContextAccessorV1 trusted,
    IOutboxOwnerV2 owner, IOutboxPublisherV2 publisher) where TContext : DbContext
{
    private readonly CallerTransactionOutboxV2 _store = new(trusted, owner);

    public async Task<IReadOnlyList<OutboxDispatchObservationV2>> DispatchBatchV2(OutboxRootKeyV2 root, Guid requestId,
        int batchSize, CancellationToken ct = default)
    {
        OutboxClaimResultV2 claim;
        PreparedClaimV2? original = null;
        try
        {
            claim = await Write(root, async tx =>
            {
                var result = await _store.ClaimBatchV2(tx, requestId, batchSize, ct); original = result.Prepared; return result;
            }, ct);
        }
        catch (Exception) { return [new("CLAIM_OUTCOME_UNKNOWN", requestId, null, null, OutboxDecisionV2.OutcomeUnknown, original?.Input)]; }
        if (claim.Prepared is null) return [new("CLAIM_REJECTED", requestId, null, null, claim.Decision)];
        OutboxClaimResultV2 confirmed;
        try { confirmed = await Read(root, tx => _store.QueryClaimV2(tx, requestId, claim.Prepared.Input, ct), ct); }
        catch (Exception) { return [new("CLAIM_OUTCOME_UNKNOWN", requestId, null, null, OutboxDecisionV2.OutcomeUnknown, claim.Prepared.Input)]; }
        if (confirmed.Decision != OutboxDecisionV2.Committed) return [new("CLAIM_OUTCOME_UNKNOWN", requestId, null, null, confirmed.Decision)];
        var observations = new List<OutboxDispatchObservationV2>();
        foreach (var ticket in confirmed.Committed!) observations.Add(await DispatchClaimV2(ticket, ticket.Ticket.ClaimReceiptRef, ct));
        return observations.AsReadOnly();
    }

    /// <summary>Supply the persisted original mutation identity on recovery. Unknown outcomes are queried, never resubmitted here.</summary>
    public async Task<OutboxDispatchObservationV2> DispatchClaimV2(CommittedClaimTicketV2 committed, Guid mutationId, CancellationToken ct = default)
    {
        OutboxEncodingV2.Key(mutationId);
        var ticket = committed.Ticket;
        string policy;
        TimeSpan budget;
        OutboxDecisionV2 gate;
        try
        {
            (gate, policy, budget) = await Write(ticket.Root, async tx =>
                (await _store.PrepareSendV2(tx, committed, mutationId, ct), tx.Registration.Policy.Version, tx.Registration.Policy.PublishBudget), ct);
        }
        catch (Exception) { return new("SEND_NOT_AUTHORIZED", ticket.ClaimRequestId, mutationId, null, OutboxDecisionV2.OutcomeUnknown); }
        if (gate != OutboxDecisionV2.PendingCommit) return new("SEND_NOT_AUTHORIZED", ticket.ClaimRequestId, mutationId, null, gate);
        // The validating transaction and its native connection have been disposed before any transport call.
        OutboxTransportResultV2 transport;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(budget);
            transport = await publisher.PublishAsync(committed.Message, timeout.Token).AsTask().WaitAsync(timeout.Token);
        }
        catch (Exception) { return new("UNCERTAIN_DELIVERY", ticket.ClaimRequestId, mutationId, null, OutboxDecisionV2.OutcomeUnknown); }
        if (transport.Decision == OutboxTransportDecisionV2.Unknown || !Enum.IsDefined(transport.Decision))
            return new("UNCERTAIN_DELIVERY", ticket.ClaimRequestId, mutationId, null, OutboxDecisionV2.OutcomeUnknown);
        var input = transport.Decision == OutboxTransportDecisionV2.Confirmed
            ? new OutboxMutationV2(mutationId, ticket, OutboxIntentV2.PublishConfirmed, policy)
            : new OutboxMutationV2(mutationId, ticket, OutboxIntentV2.RecordPublishFailure, policy, transport.SafeErrorCode, transport.Retryable, transport.SupportReference);
        try
        {
            var prepared = await Write(ticket.Root, tx => _store.CompleteClaimV2(tx, committed, input, ct), ct);
            if (prepared.Decision != OutboxDecisionV2.PendingCommit)
                return new(transport.Decision == OutboxTransportDecisionV2.Confirmed ? "UNCERTAIN_DELIVERY" : "COMPLETION_REJECTED", ticket.ClaimRequestId, mutationId, input, prepared.Decision);
            var result = await Read(ticket.Root, tx => _store.QueryClaimMutationV2(tx, mutationId, CallerTransactionOutboxV2.MutationInput(input), ct), ct);
            return new(result.Decision == OutboxDecisionV2.Committed ? result.Transition! : "UNCERTAIN_DELIVERY", ticket.ClaimRequestId, mutationId, input, result.Decision);
        }
        catch (Exception)
        {
            // Even a transport-confirmed delivery may have fully committed locally. Preserve its exact tuple.
            return new("UNCERTAIN_DELIVERY", ticket.ClaimRequestId, mutationId, input, OutboxDecisionV2.OutcomeUnknown);
        }
    }
    public Task<OutboxMutationResultV2> RecoverMutationV2(OutboxMutationV2 original, CancellationToken ct = default) =>
        Read(original.Ticket.Root, tx => _store.QueryClaimMutationV2(tx, original.MutationId, CallerTransactionOutboxV2.MutationInput(original), ct), ct);

    private async Task<T> Write<T>(OutboxRootKeyV2 root, Func<OutboxTransactionV2, Task<T>> operation, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var capability = await _store.BindTransactionAsync(db, transaction.GetDbTransaction(), root, false, ct);
        var result = await operation(capability); await transaction.CommitAsync(ct); return result;
    }
    private async Task<T> Read<T>(OutboxRootKeyV2 root, Func<OutboxTransactionV2, Task<T>> operation, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var capability = await _store.BindTransactionAsync(db, transaction.GetDbTransaction(), root, true, ct);
        return await operation(capability);
    }
}
