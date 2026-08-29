using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework;

public sealed class Cp6MessageRetentionService<TContext>
    where TContext : DbContext
{
    private readonly TContext context;
    private readonly TimeProvider timeProvider;

    public Cp6MessageRetentionService(TContext context, TimeProvider? timeProvider = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Cp6RetentionResult> DeleteExpiredAsync(
        Cp6TransactionalMessagingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var outboxDeleted = await context.Set<Cp6OutboxMessage>()
            .Where(message =>
                (message.Status == Cp6OutboxStatus.Published &&
                 message.PublishedAtUtc < now.Subtract(options.PublishedOutboxRetention)) ||
                (message.Status == Cp6OutboxStatus.DeadLettered &&
                 message.DeadLetteredAtUtc < now.Subtract(options.DeadLetterRetention)))
            .ExecuteDeleteAsync(cancellationToken);
        var inboxDeleted = await context.Set<Cp6InboxMessage>()
            .Where(message =>
                message.Status == Cp6InboxStatus.Processed &&
                ((message.OutcomeCode != Cp6InboxProcessingResult.DeadLetteredErrorCode &&
                  message.ProcessedAtUtc < now.Subtract(options.ProcessedInboxRetention)) ||
                 (message.OutcomeCode == Cp6InboxProcessingResult.DeadLetteredErrorCode &&
                  message.ProcessedAtUtc < now.Subtract(options.DeadLetterRetention))))
            .ExecuteDeleteAsync(cancellationToken);
        var deadLettersDeleted = await context.Set<Cp6DeadLetterRecord>()
            .Where(record => record.CreatedAtUtc < now.Subtract(options.DeadLetterRetention))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new Cp6RetentionResult(outboxDeleted, inboxDeleted, deadLettersDeleted);
    }
}
