using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework;

public sealed class Cp6OutboxStore<TContext>
    where TContext : DbContext
{
    private readonly TContext context;
    private readonly ICp6OutboxEnvelopeValidator validator;
    private readonly TimeProvider timeProvider;

    public Cp6OutboxStore(
        TContext context,
        ICp6OutboxEnvelopeValidator validator,
        TimeProvider? timeProvider = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Cp6OutboxMessage Enqueue(Cp6OutboxEnvelope envelope)
    {
        Cp6TransactionalMessagingGuard.Envelope(envelope);
        var validation = validator.Validate(envelope);
        ArgumentNullException.ThrowIfNull(validation);
        if (!validation.IsValid)
        {
            Cp6TransactionalMessagingGuard.ContentSafeCode(validation.ErrorCode, nameof(validation.ErrorCode));
            throw new ArgumentException($"The Outbox envelope failed validation with code '{validation.ErrorCode}'.", nameof(envelope));
        }

        var message = new Cp6OutboxMessage(envelope, timeProvider.GetUtcNow());
        context.Set<Cp6OutboxMessage>().Add(message);
        return message;
    }

    public async Task<IReadOnlyList<Cp6ClaimedOutboxMessage>> ClaimBatchAsync(
        string workerId,
        Cp6TransactionalMessagingOptions options,
        CancellationToken cancellationToken = default)
    {
        Cp6TransactionalMessagingGuard.Identifier(workerId, nameof(workerId), 128);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var now = timeProvider.GetUtcNow();
        var leaseExpiresAt = now.Add(options.OutboxLeaseDuration);
        var leaseToken = Guid.NewGuid().ToString("N");
        var candidateIds = await context.Set<Cp6OutboxMessage>()
            .AsNoTracking()
            .Where(message =>
                (message.Status == Cp6OutboxStatus.Pending && message.AvailableAtUtc <= now) ||
                (message.Status == Cp6OutboxStatus.Dispatching && message.LeaseExpiresAtUtc <= now))
            .OrderBy(message => message.AvailableAtUtc)
            .ThenBy(message => message.CreatedAtUtc)
            .Select(message => message.Id)
            .Take(options.DispatchBatchSize)
            .ToArrayAsync(cancellationToken);

        if (candidateIds.Length == 0)
        {
            return [];
        }

        await context.Set<Cp6OutboxMessage>()
            .Where(message => candidateIds.Contains(message.Id) &&
                ((message.Status == Cp6OutboxStatus.Pending && message.AvailableAtUtc <= now) ||
                 (message.Status == Cp6OutboxStatus.Dispatching && message.LeaseExpiresAtUtc <= now)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, Cp6OutboxStatus.Dispatching)
                    .SetProperty(message => message.LeaseOwner, workerId)
                    .SetProperty(message => message.LeaseToken, leaseToken)
                    .SetProperty(message => message.LeaseExpiresAtUtc, leaseExpiresAt)
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1),
                cancellationToken);

        var claimedMessages = await context.Set<Cp6OutboxMessage>()
            .AsNoTracking()
            .Where(message => candidateIds.Contains(message.Id) && message.LeaseToken == leaseToken)
            .OrderBy(message => message.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

        return claimedMessages
            .Select(message => new Cp6ClaimedOutboxMessage(
                message.ToDispatchMessage(),
                workerId,
                leaseToken,
                leaseExpiresAt))
            .ToArray();
    }

    public async Task MarkPublishedAsync(
        Cp6ClaimedOutboxMessage claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var message = await LoadOwnedClaimAsync(claim, cancellationToken);
        message.MarkPublished(timeProvider.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> MarkFailedAsync(
        Cp6ClaimedOutboxMessage claim,
        string errorCode,
        bool retryable,
        Cp6TransactionalMessagingOptions options,
        string? supportReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Cp6TransactionalMessagingGuard.ContentSafeCode(errorCode, nameof(errorCode));
        if (supportReference is not null)
        {
            Cp6TransactionalMessagingGuard.Identifier(supportReference, nameof(supportReference), 128);
        }

        var message = await LoadOwnedClaimAsync(claim, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var deadLettered = !retryable || message.AttemptCount >= options.MaxOutboxAttempts;
        if (deadLettered)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            message.DeadLetter(now, errorCode, supportReference);
            context.Set<Cp6DeadLetterRecord>().Add(new Cp6DeadLetterRecord(
                Cp6DeadLetterDirection.Outbound,
                message.TenantId,
                message.MessageId,
                null,
                message.PayloadSha256,
                errorCode,
                supportReference,
                now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        message.ScheduleRetry(now.Add(options.RetryDelay(message.AttemptCount)), errorCode, supportReference);
        await context.SaveChangesAsync(cancellationToken);
        return false;
    }

    public async Task RequeueDeadLetteredAsync(
        Guid outboxId,
        string replayReasonCode,
        CancellationToken cancellationToken = default)
    {
        if (outboxId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty Outbox id is required.", nameof(outboxId));
        }

        Cp6TransactionalMessagingGuard.ContentSafeCode(replayReasonCode, nameof(replayReasonCode));
        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var message = await context.Set<Cp6OutboxMessage>().SingleAsync(item => item.Id == outboxId, cancellationToken);
        var deadLetter = await context.Set<Cp6DeadLetterRecord>()
            .Where(record =>
                record.Direction == Cp6DeadLetterDirection.Outbound &&
                record.MessageId == message.MessageId &&
                record.ReplayedAtUtc == null)
            .OrderByDescending(record => record.CreatedAtUtc)
            .FirstAsync(cancellationToken);

        message.Requeue(now);
        deadLetter.RecordReplay(replayReasonCode, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Cp6OutboxMessage> LoadOwnedClaimAsync(
        Cp6ClaimedOutboxMessage claim,
        CancellationToken cancellationToken)
    {
        var message = await context.Set<Cp6OutboxMessage>()
            .SingleOrDefaultAsync(item =>
                item.Id == claim.Message.Id &&
                item.Status == Cp6OutboxStatus.Dispatching &&
                item.LeaseOwner == claim.LeaseOwner &&
                item.LeaseToken == claim.LeaseToken,
                cancellationToken);

        return message ?? throw new InvalidOperationException("The Outbox lease is no longer owned by this claim.");
    }
}
