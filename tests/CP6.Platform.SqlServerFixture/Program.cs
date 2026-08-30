using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using CP6.Platform.Abstractions;
using CP6.Platform.EntityFramework;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var serverConnection = Environment.GetEnvironmentVariable("CP6_P06_SQL_CONNECTION");
if (string.IsNullOrWhiteSpace(serverConnection))
{
    throw new InvalidOperationException("CP6_P06_SQL_CONNECTION is required.");
}

var databaseName = $"cp6_p06_{Guid.NewGuid():N}";
using var telemetry = new FixtureTelemetryCapture();
var serverBuilder = new SqlConnectionStringBuilder(serverConnection)
{
    InitialCatalog = "master",
    TrustServerCertificate = true
};
var databaseBuilder = new SqlConnectionStringBuilder(serverBuilder.ConnectionString)
{
    InitialCatalog = databaseName
};

await using (var connection = new SqlConnection(serverBuilder.ConnectionString))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"CREATE DATABASE [{databaseName}]";
    await command.ExecuteNonQueryAsync();
}

try
{
    var factory = new FixtureContextFactory(databaseBuilder.ConnectionString);
    await using (var setup = factory.CreateDbContext())
    {
        await setup.Database.EnsureCreatedAsync();
    }

    var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
    var validator = new AcceptingValidator();
    var options = new Cp6TransactionalMessagingOptions
    {
        OutboxLeaseDuration = TimeSpan.FromSeconds(10),
        InitialOutboxRetryDelay = TimeSpan.FromSeconds(1),
        MaximumOutboxRetryDelay = TimeSpan.FromMinutes(1),
        MaxOutboxAttempts = 2,
        MaxInboxAttempts = 2
    };
    var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    await VerifyOutboxDispatcherAsync(factory, validator, clock, tenantId);
    await VerifyAtomicOutboxAsync(factory, validator, clock, tenantId);
    await VerifyLeaseAndAtLeastOnceAsync(factory, validator, clock, options, tenantId);
    await VerifyOutboxDeadLetterReplayAsync(factory, validator, clock, options, tenantId);
    await VerifyInboxAsync(factory, validator, clock, options, tenantId);
    await VerifyRetentionAsync(factory, clock, options);
    telemetry.AssertContract();

    Console.WriteLine("P06 SQL Server fixture passed: atomic Outbox, lease/replay, Inbox idempotency/order, DLQ, retention, and P08 telemetry.");
}
finally
{
    SqlConnection.ClearAllPools();
    await using var connection = new SqlConnection(serverBuilder.ConnectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
    await command.ExecuteNonQueryAsync();
}

static async Task VerifyOutboxDispatcherAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    TimeProvider clock,
    Guid tenantId)
{
    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("telemetry-published", tenantId, "aggregate-telemetry", 1));
    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("telemetry-retry", tenantId, "aggregate-telemetry", 2));
    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("telemetry-dead-letter", tenantId, "aggregate-telemetry", 3));
    var options = new Cp6TransactionalMessagingOptions
    {
        InitialOutboxRetryDelay = TimeSpan.FromHours(1),
        MaximumOutboxRetryDelay = TimeSpan.FromHours(1),
        MaxOutboxAttempts = 2
    };
    var dispatcher = new Cp6OutboxDispatcher<FixtureDbContext>(
        factory,
        validator,
        new FixturePublisher(),
        options,
        clock);

    var result = await dispatcher.DispatchBatchAsync("worker-telemetry");

    Ensure(result == new Cp6OutboxDispatchResult(3, 1, 1, 1), "Outbox dispatcher outcomes changed while adding telemetry.");
    await using var check = factory.CreateDbContext();
    var states = await check.Set<Cp6OutboxMessage>()
        .ToDictionaryAsync(message => message.MessageId, message => message.Status);
    Ensure(states["telemetry-published"] == Cp6OutboxStatus.Published, "Successful publish was not persisted.");
    Ensure(states["telemetry-retry"] == Cp6OutboxStatus.Pending, "Retryable publish failure was not scheduled.");
    Ensure(states["telemetry-dead-letter"] == Cp6OutboxStatus.DeadLettered, "Permanent publish failure was not dead-lettered.");
}

static async Task VerifyAtomicOutboxAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    TimeProvider clock,
    Guid tenantId)
{
    await using (var rollbackContext = factory.CreateDbContext())
    await using (var transaction = await rollbackContext.Database.BeginTransactionAsync())
    {
        rollbackContext.BusinessRows.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "rolled-back-business" });
        new Cp6OutboxStore<FixtureDbContext>(rollbackContext, validator, clock).Enqueue(
            Envelope("atomic-rollback", tenantId, "aggregate-atomic", 1));
        await rollbackContext.SaveChangesAsync();
        await transaction.RollbackAsync();
    }

    await using (var check = factory.CreateDbContext())
    {
        Ensure(await check.BusinessRows.CountAsync() == 0, "Rolled-back business data remained.");
        Ensure(await check.Set<Cp6OutboxMessage>().CountAsync() == 0, "Rolled-back Outbox data remained.");
    }

    await using (var commitContext = factory.CreateDbContext())
    await using (var transaction = await commitContext.Database.BeginTransactionAsync())
    {
        commitContext.BusinessRows.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "committed-business" });
        new Cp6OutboxStore<FixtureDbContext>(commitContext, validator, clock).Enqueue(
            Envelope("atomic-commit", tenantId, "aggregate-atomic", 2));
        await commitContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    await using (var check = factory.CreateDbContext())
    {
        Ensure(await check.BusinessRows.CountAsync() == 1, "Committed business data is missing.");
        Ensure(await check.Set<Cp6OutboxMessage>().CountAsync() == 1, "Committed Outbox data is missing.");
    }
}

static async Task VerifyLeaseAndAtLeastOnceAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    MutableTimeProvider clock,
    Cp6TransactionalMessagingOptions options,
    Guid tenantId)
{
    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("lease-race", tenantId, "aggregate-lease", 1));
    var firstWorker = ClaimAsync(factory, validator, clock, options, "worker-race-a");
    var secondWorker = ClaimAsync(factory, validator, clock, options, "worker-race-b");
    var competingClaims = await Task.WhenAll(firstWorker, secondWorker);
    var raceClaims = competingClaims.SelectMany(claims => claims).Where(claim => claim.Message.MessageId == "lease-race").ToArray();
    Ensure(raceClaims.Length == 1, "Competing workers did not produce exactly one lease owner.");
    await MarkPublishedAsync(factory, validator, clock, raceClaims[0]);

    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("ack-then-crash", tenantId, "aggregate-crash", 1));
    var originalClaim = AssertSingleByMessageId(
        await ClaimAsync(factory, validator, clock, options, "worker-before-crash"),
        "ack-then-crash");
    clock.Advance(TimeSpan.FromSeconds(11));
    var recoveredClaim = AssertSingleByMessageId(
        await ClaimAsync(factory, validator, clock, options, "worker-after-crash"),
        "ack-then-crash");
    Ensure(recoveredClaim.Message.AttemptCount == 2, "Expired lease was not re-delivered at least once.");

    await using (var staleContext = factory.CreateDbContext())
    {
        var staleStore = new Cp6OutboxStore<FixtureDbContext>(staleContext, validator, clock);
        await EnsureThrowsAsync<InvalidOperationException>(() => staleStore.MarkPublishedAsync(originalClaim));
    }

    await MarkPublishedAsync(factory, validator, clock, recoveredClaim);
}

static async Task VerifyOutboxDeadLetterReplayAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    MutableTimeProvider clock,
    Cp6TransactionalMessagingOptions options,
    Guid tenantId)
{
    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("outbox-replay", tenantId, "aggregate-replay", 1));
    var claim = AssertSingleByMessageId(
        await ClaimAsync(factory, validator, clock, options, "worker-dlq"),
        "outbox-replay");
    await using (var failureContext = factory.CreateDbContext())
    {
        var store = new Cp6OutboxStore<FixtureDbContext>(failureContext, validator, clock);
        Ensure(await store.MarkFailedAsync(claim, "CP6_TEST_PERMANENT", false, options), "Permanent failure was not dead-lettered.");
    }

    await using (var replayContext = factory.CreateDbContext())
    {
        var store = new Cp6OutboxStore<FixtureDbContext>(replayContext, validator, clock);
        await store.RequeueDeadLetteredAsync(claim.Message.Id, "AUTHORIZED_REPLAY");
    }

    await using (var check = factory.CreateDbContext())
    {
        var message = await check.Set<Cp6OutboxMessage>().SingleAsync(item => item.Id == claim.Message.Id);
        var deadLetter = await check.Set<Cp6DeadLetterRecord>().SingleAsync(item => item.MessageId == "outbox-replay");
        Ensure(message.MessageId == "outbox-replay" && message.Status == Cp6OutboxStatus.Pending, "Outbox replay changed identity or state.");
        Ensure(deadLetter.ReplayedAtUtc is not null && deadLetter.ReplayReasonCode == "AUTHORIZED_REPLAY", "Outbox replay audit is missing.");
    }

    await EnqueueAndSaveAsync(factory, validator, clock, Envelope("outbox-retained-dlq", tenantId, "aggregate-retention", 1));
    var retainedClaim = AssertSingleByMessageId(
        await ClaimAsync(factory, validator, clock, options, "worker-retained-dlq"),
        "outbox-retained-dlq");
    await using var retainedContext = factory.CreateDbContext();
    await new Cp6OutboxStore<FixtureDbContext>(retainedContext, validator, clock)
        .MarkFailedAsync(retainedClaim, "CP6_TEST_RETAINED", false, options);
}

static async Task VerifyInboxAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    MutableTimeProvider clock,
    Cp6TransactionalMessagingOptions options,
    Guid tenantId)
{
    var processor = new Cp6InboxProcessor<FixtureDbContext>(factory, validator, options, clock);
    var invalid = await new Cp6InboxProcessor<FixtureDbContext>(factory, new RejectingInboxValidator(), options, clock)
        .ProcessAsync(
            Delivery("inbox-invalid", tenantId, "aggregate-invalid", 1, "invalid"),
            (_, _) => throw new InvalidOperationException("Invalid Inbox delivery reached the handler."));
    Ensure(invalid.Disposition == Cp6InboxDisposition.Invalid, "Invalid Inbox delivery was not rejected before the transaction.");
    var appliedCalls = 0;
    var firstDelivery = Delivery("inbox-applied", tenantId, "aggregate-inbox", 1, "payload-v1");
    var first = await processor.ProcessAsync(firstDelivery, async (context, cancellationToken) =>
    {
        appliedCalls++;
        context.BusinessRows.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "inbox-business-v1" });
        new Cp6OutboxStore<FixtureDbContext>(context, validator, clock).Enqueue(
            Envelope("inbox-result-v1", tenantId, "aggregate-inbox-result", 1));
        await Task.CompletedTask;
    });
    Ensure(first.Disposition == Cp6InboxDisposition.Applied, "First Inbox delivery was not applied.");

    var duplicate = await processor.ProcessAsync(firstDelivery, (_, _) =>
    {
        appliedCalls++;
        return Task.CompletedTask;
    });
    Ensure(duplicate.Disposition == Cp6InboxDisposition.Duplicate && appliedCalls == 1, "Duplicate Inbox delivery ran the handler.");

    var conflict = await processor.ProcessAsync(
        firstDelivery with { Payload = "different-payload"u8.ToArray() },
        (_, _) => throw new InvalidOperationException("Conflict handler must not run."));
    Ensure(conflict.Disposition == Cp6InboxDisposition.PayloadConflict, "Inbox payload hash conflict was not rejected.");

    var outOfOrder = await processor.ProcessAsync(
        Delivery("inbox-out-of-order", tenantId, "aggregate-inbox", 1, "old-payload"),
        (_, _) => throw new InvalidOperationException("Out-of-order handler must not run."));
    Ensure(outOfOrder.Disposition == Cp6InboxDisposition.IgnoredOutOfOrder, "Out-of-order aggregate version was not ignored.");

    var poisonDelivery = Delivery("inbox-poison", tenantId, "aggregate-inbox", 2, "poison-payload");
    async Task PoisonHandler(FixtureDbContext context, CancellationToken cancellationToken)
    {
        context.BusinessRows.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "must-roll-back" });
        new Cp6OutboxStore<FixtureDbContext>(context, validator, clock).Enqueue(
            Envelope("must-roll-back-result", tenantId, "aggregate-inbox-result", 2));
        await context.SaveChangesAsync(cancellationToken);
        throw new Cp6InboxProcessingException("CP6_TEST_POISON", true, "fixture");
    }

    var firstFailure = await processor.ProcessAsync(poisonDelivery, PoisonHandler);
    Ensure(firstFailure.Disposition == Cp6InboxDisposition.RetryScheduled, "First poison failure did not schedule retry.");
    await AssertRolledBackAsync(factory);
    var secondFailure = await processor.ProcessAsync(poisonDelivery, PoisonHandler);
    Ensure(secondFailure.Disposition == Cp6InboxDisposition.DeadLettered, "Bounded poison retries did not dead-letter.");
    await AssertRolledBackAsync(factory);

    await processor.RequeueDeadLetteredAsync("crm-projection", "inbox-poison", "AUTHORIZED_REPLAY");
    var replay = await processor.ProcessAsync(poisonDelivery, (context, _) =>
    {
        context.BusinessRows.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "replayed-business" });
        return Task.CompletedTask;
    });
    Ensure(replay.Disposition == Cp6InboxDisposition.Applied, "Authorized Inbox replay did not apply.");

    await using var check = factory.CreateDbContext();
    Ensure(await check.BusinessRows.CountAsync(row => row.Name == "replayed-business") == 1, "Replayed Inbox side effect is missing.");
    var poisonInbox = await check.Set<Cp6InboxMessage>().SingleAsync(item => item.MessageId == "inbox-poison");
    Ensure(poisonInbox.AttemptCount == 1 && poisonInbox.Status == Cp6InboxStatus.Processed, "Inbox replay did not reset the bounded attempt state.");
    var poisonDeadLetter = await check.Set<Cp6DeadLetterRecord>()
        .SingleAsync(item => item.Direction == Cp6DeadLetterDirection.Inbound && item.MessageId == "inbox-poison");
    Ensure(poisonDeadLetter.ReplayedAtUtc is not null, "Inbox replay audit is missing.");
    Ensure(!poisonDeadLetter.ErrorCode.Contains("poison-payload", StringComparison.Ordinal), "Dead letter exposed payload content.");

    var retainedDeadLetter = await processor.ProcessAsync(
        Delivery("inbox-retained-dlq", tenantId, "aggregate-retained-dlq", 1, "retained-payload"),
        (_, _) => throw new Cp6InboxProcessingException("CP6_TEST_RETAINED", false));
    Ensure(retainedDeadLetter.Disposition == Cp6InboxDisposition.DeadLettered, "Non-retryable Inbox failure was not dead-lettered.");
}

static async Task VerifyRetentionAsync(
    FixtureContextFactory factory,
    MutableTimeProvider clock,
    Cp6TransactionalMessagingOptions options)
{
    clock.Advance(TimeSpan.FromDays(8));
    await using (var context = factory.CreateDbContext())
    {
        var result = await new Cp6MessageRetentionService<FixtureDbContext>(context, clock).DeleteExpiredAsync(options);
        Ensure(result.OutboxDeleted > 0 && result.InboxDeleted == 0 && result.DeadLettersDeleted == 0, "Seven-day published Outbox retention failed.");
        Ensure(await context.Set<Cp6OutboxMessage>().AnyAsync(item => item.Status == Cp6OutboxStatus.Pending), "Retention deleted pending Outbox work.");
    }

    clock.Advance(TimeSpan.FromDays(23));
    await using (var context = factory.CreateDbContext())
    {
        var result = await new Cp6MessageRetentionService<FixtureDbContext>(context, clock).DeleteExpiredAsync(options);
        Ensure(result.InboxDeleted > 0 && result.DeadLettersDeleted == 0, "Thirty-day Inbox retention failed.");
        Ensure(
            await context.Set<Cp6InboxMessage>().AnyAsync(item => item.MessageId == "inbox-retained-dlq"),
            "Thirty-day Inbox retention deleted a replayable dead-letter before its ninety-day window.");
    }

    clock.Advance(TimeSpan.FromDays(60));
    await using (var context = factory.CreateDbContext())
    {
        var result = await new Cp6MessageRetentionService<FixtureDbContext>(context, clock).DeleteExpiredAsync(options);
        Ensure(result.OutboxDeleted > 0 && result.InboxDeleted > 0 && result.DeadLettersDeleted > 0, "Ninety-day dead-letter retention failed.");
        Ensure(
            !await context.Set<Cp6InboxMessage>().AnyAsync(item => item.MessageId == "inbox-retained-dlq"),
            "Ninety-day retention left an expired dead-lettered Inbox record.");
    }
}

static async Task AssertRolledBackAsync(FixtureContextFactory factory)
{
    await using var context = factory.CreateDbContext();
    Ensure(!await context.BusinessRows.AnyAsync(row => row.Name == "must-roll-back"), "Failed Inbox business change was committed.");
    Ensure(!await context.Set<Cp6OutboxMessage>().AnyAsync(item => item.MessageId == "must-roll-back-result"), "Failed Inbox result Outbox was committed.");
}

static async Task EnqueueAndSaveAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    TimeProvider clock,
    Cp6OutboxEnvelope envelope)
{
    await using var context = factory.CreateDbContext();
    new Cp6OutboxStore<FixtureDbContext>(context, validator, clock).Enqueue(envelope);
    await context.SaveChangesAsync();
}

static async Task<IReadOnlyList<Cp6ClaimedOutboxMessage>> ClaimAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    TimeProvider clock,
    Cp6TransactionalMessagingOptions options,
    string workerId)
{
    await using var context = factory.CreateDbContext();
    return await new Cp6OutboxStore<FixtureDbContext>(context, validator, clock).ClaimBatchAsync(workerId, options);
}

static async Task MarkPublishedAsync(
    FixtureContextFactory factory,
    AcceptingValidator validator,
    TimeProvider clock,
    Cp6ClaimedOutboxMessage claim)
{
    await using var context = factory.CreateDbContext();
    await new Cp6OutboxStore<FixtureDbContext>(context, validator, clock).MarkPublishedAsync(claim);
}

static Cp6ClaimedOutboxMessage AssertSingleByMessageId(
    IReadOnlyList<Cp6ClaimedOutboxMessage> claims,
    string messageId)
{
    var matches = claims.Where(claim => claim.Message.MessageId == messageId).ToArray();
    Ensure(matches.Length == 1, $"Expected one claim for '{messageId}' but found {matches.Length}.");
    return matches[0];
}

static Cp6OutboxEnvelope Envelope(string messageId, Guid tenantId, string aggregateId, int aggregateVersion) =>
    new(
        messageId,
        tenantId,
        "cp6.platform.contract-example-changed.v1",
        $"{tenantId:D}/{aggregateId}",
        System.Text.Encoding.UTF8.GetBytes($"payload-{messageId}"),
        $"correlation-{messageId}",
        $"causation-{messageId}",
        aggregateId,
        aggregateVersion);

static Cp6InboxDelivery Delivery(string messageId, Guid tenantId, string aggregateId, int aggregateVersion, string payload) =>
    new(
        "crm-projection",
        messageId,
        tenantId,
        "cp6.platform.contract-example-changed.v1",
        $"{tenantId:D}/{aggregateId}",
        System.Text.Encoding.UTF8.GetBytes(payload),
        aggregateId,
        aggregateVersion);

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task EnsureThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

internal sealed class FixtureContextFactory(string connectionString) : IDbContextFactory<FixtureDbContext>
{
    public FixtureDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FixtureDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new FixtureDbContext(options);
    }
}

internal sealed class FixtureDbContext(DbContextOptions<FixtureDbContext> options) : DbContext(options)
{
    public DbSet<BusinessRow> BusinessRows => Set<BusinessRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessRow>(entity =>
        {
            entity.ToTable("BusinessRow");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Name).HasMaxLength(128).IsRequired();
        });
        modelBuilder.AddCp6TransactionalMessaging();
    }
}

internal sealed class BusinessRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class AcceptingValidator : ICp6OutboxEnvelopeValidator, ICp6InboxDeliveryValidator
{
    public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope) => Cp6MessageValidationResult.Valid;

    public Cp6MessageValidationResult Validate(Cp6InboxDelivery delivery) => Cp6MessageValidationResult.Valid;
}

internal sealed class RejectingInboxValidator : ICp6InboxDeliveryValidator
{
    public Cp6MessageValidationResult Validate(Cp6InboxDelivery delivery) => Cp6MessageValidationResult.Invalid();
}

internal sealed class FixturePublisher : ICp6OutboxPublisher
{
    public Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default) =>
        message.MessageId switch
        {
            "telemetry-published" => Task.CompletedTask,
            "telemetry-retry" => Task.FromException(
                new Cp6OutboxPublishException("CP6_TEST_RETRYABLE", true, "fixture")),
            "telemetry-dead-letter" => Task.FromException(
                new Cp6OutboxPublishException("CP6_TEST_PERMANENT", false, "fixture")),
            _ => throw new InvalidOperationException("Unexpected Outbox message reached the telemetry publisher.")
        };
}

internal sealed class FixtureTelemetryCapture : IDisposable
{
    private readonly ActivityListener activityListener;
    private readonly MeterListener meterListener;

    public FixtureTelemetryCapture()
    {
        activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Cp6TelemetrySources.EntityFramework,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Cp6TelemetryMeters.EntityFramework)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Enqueue(new FixtureMeasurement(instrument.Name, value, tags.ToArray())));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            Measurements.Enqueue(new FixtureMeasurement(instrument.Name, value, tags.ToArray())));
        meterListener.Start();
    }

    private ConcurrentQueue<Activity> Activities { get; } = [];

    private ConcurrentQueue<FixtureMeasurement> Measurements { get; } = [];

    public void AssertContract()
    {
        var dispositions = Activities
            .Select(activity => activity.GetTagItem(Cp6TelemetryConventions.MessagingDispositionTag)?.ToString())
            .Where(disposition => disposition is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[]
        {
            "enqueue", "claim", "published", "retry_scheduled", "dead_lettered", "replayed", "retained",
            "invalid", "duplicate", "payload_conflict", "applied", "ignored_out_of_order"
        })
        {
            Require(dispositions.Contains(required), $"P08 telemetry disposition '{required}' was not observed through the real SQL fixture.");
        }

        Require(Activities.All(activity =>
                activity.OperationName is Cp6TelemetryConventions.OutboxDispatchOperation or Cp6TelemetryConventions.InboxProcessOperation),
            "An unapproved Entity Framework activity name was emitted.");
        Require(Activities.SelectMany(activity => activity.TagObjects).All(tag => Cp6TelemetryConventions.AllowedMetricTags.Contains(tag.Key)),
            "An Entity Framework activity emitted a non-allowlisted tag.");
        Require(Measurements.SelectMany(measurement => measurement.Tags).All(tag => Cp6TelemetryConventions.AllowedMetricTags.Contains(tag.Key)),
            "An Entity Framework metric emitted a non-allowlisted tag.");
        Require(Measurements.Any(measurement => measurement.InstrumentName == "cp6.outbox.oldest_available.age" && measurement.Value >= 0),
            "Oldest available Outbox age was not recorded as a measurement.");
        Require(Measurements.Any(measurement => measurement.InstrumentName == "cp6.messaging.attempts" && measurement.Value >= 1),
            "Messaging attempt count was not recorded as a measurement.");
        Require(Measurements.SelectMany(measurement => measurement.Tags).All(tag =>
                !tag.Key.Contains("age", StringComparison.OrdinalIgnoreCase) &&
                !tag.Key.Contains("attempt", StringComparison.OrdinalIgnoreCase)),
            "Age or attempt cardinality leaked into metric labels.");
    }

    public void Dispose()
    {
        meterListener.Dispose();
        activityListener.Dispose();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record FixtureMeasurement(
    string InstrumentName,
    double Value,
    KeyValuePair<string, object?>[] Tags);

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
}
