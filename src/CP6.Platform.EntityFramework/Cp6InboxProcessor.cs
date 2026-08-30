using System.Data;
using System.Diagnostics;
using CP6.Platform.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework;

public sealed class Cp6InboxProcessor<TContext>
    where TContext : DbContext
{
    public const string UnexpectedProcessingErrorCode = "CP6_INBOX_PROCESSING_FAILED";

    private readonly IDbContextFactory<TContext> contextFactory;
    private readonly ICp6InboxDeliveryValidator validator;
    private readonly Cp6TransactionalMessagingOptions options;
    private readonly TimeProvider timeProvider;

    public Cp6InboxProcessor(
        IDbContextFactory<TContext> contextFactory,
        ICp6InboxDeliveryValidator validator,
        Cp6TransactionalMessagingOptions options,
        TimeProvider? timeProvider = null)
    {
        this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Cp6InboxProcessingResult> ProcessAsync(
        Cp6InboxDelivery delivery,
        Func<TContext, CancellationToken, Task> applyDatabaseChangesAsync,
        CancellationToken cancellationToken = default)
    {
        Cp6TransactionalMessagingGuard.Delivery(delivery);
        ArgumentNullException.ThrowIfNull(applyDatabaseChangesAsync);
        var payloadSha256 = Cp6TransactionalMessagingGuard.Sha256(delivery.Payload);
        using var telemetry = Cp6EntityFrameworkTelemetry.StartInbox(ActivityKind.Consumer);
        var validation = validator.Validate(delivery);
        ArgumentNullException.ThrowIfNull(validation);
        if (!validation.IsValid)
        {
            Cp6TransactionalMessagingGuard.ContentSafeCode(validation.ErrorCode, nameof(validation.ErrorCode));
            return CompleteTelemetry(
                telemetry,
                new Cp6InboxProcessingResult(Cp6InboxDisposition.Invalid, validation.ErrorCode, payloadSha256));
        }

        try
        {
            return CompleteTelemetry(
                telemetry,
                await ProcessValidatedAsync(delivery, payloadSha256, applyDatabaseChangesAsync, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            telemetry.Cancelled("cancelled");
            throw;
        }
        catch (Cp6InboxProcessingException exception)
        {
            return CompleteTelemetry(
                telemetry,
                await RecordProcessingFailureAsync(
                    delivery,
                    payloadSha256,
                    exception.ErrorCode,
                    exception.Retryable,
                    exception.SupportReference,
                    cancellationToken));
        }
        catch (DbUpdateException exception)
        {
            await using var readContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await readContext.Set<Cp6InboxMessage>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    message => message.ConsumerName == delivery.ConsumerName && message.MessageId == delivery.MessageId,
                    cancellationToken);
            if (existing is null)
            {
                return CompleteTelemetry(
                    telemetry,
                    await RecordProcessingFailureAsync(
                        delivery,
                        payloadSha256,
                        UnexpectedProcessingErrorCode,
                        true,
                        exception.GetType().Name,
                        cancellationToken));
            }

            if (existing.PayloadSha256 == payloadSha256 && existing.Status == Cp6InboxStatus.Processed)
            {
                return CompleteTelemetry(
                    telemetry,
                    new Cp6InboxProcessingResult(Cp6InboxDisposition.Duplicate, string.Empty, payloadSha256));
            }

            if (existing.PayloadSha256 == payloadSha256)
            {
                return CompleteTelemetry(
                    telemetry,
                    await RecordProcessingFailureAsync(
                        delivery,
                        payloadSha256,
                        UnexpectedProcessingErrorCode,
                        true,
                        exception.GetType().Name,
                        cancellationToken));
            }

            return CompleteTelemetry(
                telemetry,
                await RecordPayloadConflictAsync(delivery, payloadSha256, cancellationToken));
        }
        catch (Exception exception)
        {
            return CompleteTelemetry(
                telemetry,
                await RecordProcessingFailureAsync(
                    delivery,
                    payloadSha256,
                    UnexpectedProcessingErrorCode,
                    true,
                    exception.GetType().Name,
                    cancellationToken));
        }
    }

    private async Task<Cp6InboxProcessingResult> ProcessValidatedAsync(
        Cp6InboxDelivery delivery,
        string payloadSha256,
        Func<TContext, CancellationToken, Task> applyDatabaseChangesAsync,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await context.Set<Cp6InboxMessage>()
            .SingleOrDefaultAsync(
                message => message.ConsumerName == delivery.ConsumerName && message.MessageId == delivery.MessageId,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadSha256 != payloadSha256)
            {
                context.Set<Cp6DeadLetterRecord>().Add(CreateInboundDeadLetter(
                    delivery,
                    payloadSha256,
                    Cp6InboxProcessingResult.PayloadConflictErrorCode,
                    null));
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new Cp6InboxProcessingResult(
                    Cp6InboxDisposition.PayloadConflict,
                    Cp6InboxProcessingResult.PayloadConflictErrorCode,
                    payloadSha256);
            }

            if (existing.Status == Cp6InboxStatus.Processed)
            {
                return new Cp6InboxProcessingResult(Cp6InboxDisposition.Duplicate, string.Empty, payloadSha256);
            }

            existing.BeginRetry();
        }

        var now = timeProvider.GetUtcNow();
        var inbox = existing ?? new Cp6InboxMessage(delivery, payloadSha256, now);
        if (existing is null)
        {
            context.Set<Cp6InboxMessage>().Add(inbox);
        }
        var checkpoint = await context.Set<Cp6InboxAggregateCheckpoint>()
            .SingleOrDefaultAsync(
                item => item.ConsumerName == delivery.ConsumerName &&
                    item.TenantId == delivery.TenantId &&
                    item.AggregateId == delivery.AggregateId,
                cancellationToken);
        if (checkpoint is not null && delivery.AggregateVersion <= checkpoint.AggregateVersion)
        {
            inbox.Complete(now, Cp6InboxProcessingResult.OutOfOrderErrorCode);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new Cp6InboxProcessingResult(
                Cp6InboxDisposition.IgnoredOutOfOrder,
                Cp6InboxProcessingResult.OutOfOrderErrorCode,
                payloadSha256);
        }

        await applyDatabaseChangesAsync(context, cancellationToken);
        if (checkpoint is null)
        {
            context.Set<Cp6InboxAggregateCheckpoint>().Add(new Cp6InboxAggregateCheckpoint(delivery, now));
        }
        else
        {
            checkpoint.Advance(delivery.AggregateVersion, now);
        }

        inbox.Complete(now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Cp6InboxProcessingResult(Cp6InboxDisposition.Applied, string.Empty, payloadSha256);
    }

    private async Task<Cp6InboxProcessingResult> RecordPayloadConflictAsync(
        Cp6InboxDelivery delivery,
        string payloadSha256,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Set<Cp6DeadLetterRecord>().Add(CreateInboundDeadLetter(
            delivery,
            payloadSha256,
            Cp6InboxProcessingResult.PayloadConflictErrorCode,
            null));
        await context.SaveChangesAsync(cancellationToken);
        return new Cp6InboxProcessingResult(
            Cp6InboxDisposition.PayloadConflict,
            Cp6InboxProcessingResult.PayloadConflictErrorCode,
            payloadSha256);
    }

    public async Task RequeueDeadLetteredAsync(
        string consumerName,
        string messageId,
        string replayReasonCode,
        CancellationToken cancellationToken = default)
    {
        Cp6TransactionalMessagingGuard.Identifier(consumerName, nameof(consumerName), 128);
        Cp6TransactionalMessagingGuard.Identifier(messageId, nameof(messageId), 128);
        Cp6TransactionalMessagingGuard.ContentSafeCode(replayReasonCode, nameof(replayReasonCode));

        using var telemetry = Cp6EntityFrameworkTelemetry.StartInbox();
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var inbox = await context.Set<Cp6InboxMessage>().SingleAsync(
                message => message.ConsumerName == consumerName && message.MessageId == messageId,
                cancellationToken);
            var deadLetter = await context.Set<Cp6DeadLetterRecord>()
                .Where(record =>
                    record.Direction == Cp6DeadLetterDirection.Inbound &&
                    record.ConsumerName == consumerName &&
                    record.MessageId == messageId &&
                    record.ReplayedAtUtc == null)
                .OrderByDescending(record => record.CreatedAtUtc)
                .FirstAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            inbox.Requeue();
            deadLetter.RecordReplay(replayReasonCode, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            telemetry.Success("replayed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            telemetry.Cancelled("replayed");
            throw;
        }
        catch (Exception)
        {
            telemetry.Failure("processing_failure", "failed");
            throw;
        }
    }

    private async Task<Cp6InboxProcessingResult> RecordProcessingFailureAsync(
        Cp6InboxDelivery delivery,
        string payloadSha256,
        string errorCode,
        bool retryable,
        string? supportReference,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var inbox = await context.Set<Cp6InboxMessage>().SingleOrDefaultAsync(
            message => message.ConsumerName == delivery.ConsumerName && message.MessageId == delivery.MessageId,
            cancellationToken);
        if (inbox is null)
        {
            inbox = new Cp6InboxMessage(delivery, payloadSha256, timeProvider.GetUtcNow());
            context.Set<Cp6InboxMessage>().Add(inbox);
        }
        else if (inbox.PayloadSha256 != payloadSha256)
        {
            return new Cp6InboxProcessingResult(
                Cp6InboxDisposition.PayloadConflict,
                Cp6InboxProcessingResult.PayloadConflictErrorCode,
                payloadSha256);
        }
        else if (inbox.Status == Cp6InboxStatus.Processed)
        {
            return new Cp6InboxProcessingResult(Cp6InboxDisposition.Duplicate, string.Empty, payloadSha256);
        }
        else
        {
            inbox.BeginRetry();
        }

        var now = timeProvider.GetUtcNow();
        var deadLettered = !retryable || inbox.AttemptCount >= options.MaxInboxAttempts;
        if (deadLettered)
        {
            inbox.DeadLetter(now, errorCode, supportReference);
            context.Set<Cp6DeadLetterRecord>().Add(CreateInboundDeadLetter(
                delivery,
                payloadSha256,
                errorCode,
                supportReference));
        }
        else
        {
            inbox.RecordFailure(errorCode, supportReference);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Cp6EntityFrameworkTelemetry.RecordAttemptCount(
            inbox.AttemptCount,
            Cp6TelemetryConventions.InboxProcessOperation,
            deadLettered ? "dead_lettered" : "retry_scheduled");
        return new Cp6InboxProcessingResult(
            deadLettered ? Cp6InboxDisposition.DeadLettered : Cp6InboxDisposition.RetryScheduled,
            errorCode,
            payloadSha256);
    }

    private Cp6DeadLetterRecord CreateInboundDeadLetter(
        Cp6InboxDelivery delivery,
        string payloadSha256,
        string errorCode,
        string? supportReference) =>
        new(
            Cp6DeadLetterDirection.Inbound,
            delivery.TenantId,
            delivery.MessageId,
            delivery.ConsumerName,
            payloadSha256,
            errorCode,
            supportReference,
            timeProvider.GetUtcNow());

    private static Cp6InboxProcessingResult CompleteTelemetry(
        Cp6EntityFrameworkTelemetry.OperationScope telemetry,
        Cp6InboxProcessingResult result)
    {
        switch (result.Disposition)
        {
            case Cp6InboxDisposition.Applied:
                telemetry.Success("applied");
                break;
            case Cp6InboxDisposition.Duplicate:
                telemetry.Success("duplicate");
                break;
            case Cp6InboxDisposition.IgnoredOutOfOrder:
                telemetry.Rejected("out_of_order", "ignored_out_of_order");
                break;
            case Cp6InboxDisposition.PayloadConflict:
                telemetry.Rejected("payload_conflict", "payload_conflict");
                break;
            case Cp6InboxDisposition.Invalid:
                telemetry.Rejected("validation_failed", "invalid");
                break;
            case Cp6InboxDisposition.RetryScheduled:
                telemetry.Failure("processing_failure", "retry_scheduled");
                break;
            case Cp6InboxDisposition.DeadLettered:
                telemetry.Failure("processing_failure", "dead_lettered");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), "Inbox disposition is not supported.");
        }

        return result;
    }
}
