using Microsoft.EntityFrameworkCore;
using CP6.Platform.EntityFramework.OutboxV2;

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
        using var outboxTelemetry = Cp6EntityFrameworkTelemetry.StartOutbox();
        using var inboxTelemetry = Cp6EntityFrameworkTelemetry.StartInbox();
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var outbox = await LegacyOutboxFenceV2.Unbound<Cp6OutboxMessage>(context, "Id", cancellationToken);
            var inbox = await LegacyOutboxFenceV2.Unbound<Cp6InboxMessage>(context, "MessageId", cancellationToken);
            var deadLetters = await LegacyOutboxFenceV2.Unbound<Cp6DeadLetterRecord>(context, "MessageId", cancellationToken);
            var outboxDeleted = await outbox
                .Where(message =>
                    (message.Status == Cp6OutboxStatus.Published &&
                     message.PublishedAtUtc < now.Subtract(options.PublishedOutboxRetention)) ||
                    (message.Status == Cp6OutboxStatus.DeadLettered &&
                     message.DeadLetteredAtUtc < now.Subtract(options.DeadLetterRetention)))
                .ExecuteDeleteAsync(cancellationToken);
            var inboxDeleted = await inbox
                .Where(message =>
                    message.Status == Cp6InboxStatus.Processed &&
                    ((message.OutcomeCode != Cp6InboxProcessingResult.DeadLetteredErrorCode &&
                      message.ProcessedAtUtc < now.Subtract(options.ProcessedInboxRetention)) ||
                     (message.OutcomeCode == Cp6InboxProcessingResult.DeadLetteredErrorCode &&
                      message.ProcessedAtUtc < now.Subtract(options.DeadLetterRetention))))
                .ExecuteDeleteAsync(cancellationToken);
            var deadLettersDeleted = await deadLetters
                .Where(record => record.CreatedAtUtc < now.Subtract(options.DeadLetterRetention))
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            outboxTelemetry.Success("retained");
            inboxTelemetry.Success("retained");
            return new Cp6RetentionResult(outboxDeleted, inboxDeleted, deadLettersDeleted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outboxTelemetry.Cancelled("retained");
            inboxTelemetry.Cancelled("retained");
            throw;
        }
        catch (Exception)
        {
            outboxTelemetry.Failure("processing_failure", "failed");
            inboxTelemetry.Failure("processing_failure", "failed");
            throw;
        }
    }
}
