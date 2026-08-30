using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework;

public sealed class Cp6OutboxDispatcher<TContext>
    where TContext : DbContext
{
    public const string UnexpectedPublishErrorCode = "CP6_OUTBOX_PUBLISH_FAILED";

    private readonly IDbContextFactory<TContext> contextFactory;
    private readonly ICp6OutboxEnvelopeValidator validator;
    private readonly ICp6OutboxPublisher publisher;
    private readonly Cp6TransactionalMessagingOptions options;
    private readonly TimeProvider timeProvider;

    public Cp6OutboxDispatcher(
        IDbContextFactory<TContext> contextFactory,
        ICp6OutboxEnvelopeValidator validator,
        ICp6OutboxPublisher publisher,
        Cp6TransactionalMessagingOptions options,
        TimeProvider? timeProvider = null)
    {
        this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Cp6OutboxDispatchResult> DispatchBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Cp6ClaimedOutboxMessage> claims;
        await using (var claimContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var claimStore = new Cp6OutboxStore<TContext>(claimContext, validator, timeProvider);
            claims = await claimStore.ClaimBatchAsync(workerId, options, cancellationToken);
        }

        var published = 0;
        var retryScheduled = 0;
        var deadLettered = 0;
        foreach (var claim in claims)
        {
            using var telemetry = Cp6EntityFrameworkTelemetry.StartOutbox(ActivityKind.Producer);
            try
            {
                await publisher.PublishAsync(claim.Message, cancellationToken);
                await using var successContext = await contextFactory.CreateDbContextAsync(cancellationToken);
                var successStore = new Cp6OutboxStore<TContext>(successContext, validator, timeProvider);
                await successStore.MarkPublishedAsync(claim, cancellationToken);
                published++;
                telemetry.Success("published");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                telemetry.Cancelled("cancelled");
                throw;
            }
            catch (Cp6OutboxPublishException exception)
            {
                var wasDeadLettered = await MarkFailedAsync(claim, exception.ErrorCode, exception.Retryable, exception.SupportReference, cancellationToken);
                deadLettered += wasDeadLettered ? 1 : 0;
                retryScheduled += wasDeadLettered ? 0 : 1;
                telemetry.Failure("publish_failure", wasDeadLettered ? "dead_lettered" : "retry_scheduled");
            }
            catch (Exception exception)
            {
                var supportReference = exception.GetType().Name;
                var wasDeadLettered = await MarkFailedAsync(claim, UnexpectedPublishErrorCode, true, supportReference, cancellationToken);
                deadLettered += wasDeadLettered ? 1 : 0;
                retryScheduled += wasDeadLettered ? 0 : 1;
                telemetry.Failure("publish_failure", wasDeadLettered ? "dead_lettered" : "retry_scheduled");
            }
        }

        return new Cp6OutboxDispatchResult(claims.Count, published, retryScheduled, deadLettered);
    }

    private async Task<bool> MarkFailedAsync(
        Cp6ClaimedOutboxMessage claim,
        string errorCode,
        bool retryable,
        string? supportReference,
        CancellationToken cancellationToken)
    {
        await using var failureContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var failureStore = new Cp6OutboxStore<TContext>(failureContext, validator, timeProvider);
        return await failureStore.MarkFailedAsync(
            claim,
            errorCode,
            retryable,
            options,
            supportReference,
            cancellationToken);
    }
}
