namespace CP6.Platform.EntityFramework;

public sealed class Cp6OutboxMessage
{
    private Cp6OutboxMessage()
    {
    }

    internal Cp6OutboxMessage(Cp6OutboxEnvelope envelope, DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        MessageId = envelope.MessageId;
        TenantId = envelope.TenantId;
        TopicName = envelope.TopicName;
        PartitionKey = envelope.PartitionKey;
        Payload = envelope.Payload.ToArray();
        PayloadSha256 = Cp6TransactionalMessagingGuard.Sha256(envelope.Payload);
        CorrelationId = envelope.CorrelationId;
        CausationId = envelope.CausationId;
        AggregateId = envelope.AggregateId;
        AggregateVersion = envelope.AggregateVersion;
        Status = Cp6OutboxStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        AvailableAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string MessageId { get; private set; } = string.Empty;

    public Guid TenantId { get; private set; }

    public string TopicName { get; private set; } = string.Empty;

    public string PartitionKey { get; private set; } = string.Empty;

    public byte[] Payload { get; private set; } = [];

    public string PayloadSha256 { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public string CausationId { get; private set; } = string.Empty;

    public string AggregateId { get; private set; } = string.Empty;

    public int AggregateVersion { get; private set; }

    public Cp6OutboxStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset AvailableAtUtc { get; private set; }

    public string? LeaseOwner { get; private set; }

    public string? LeaseToken { get; private set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }

    public string? LastErrorCode { get; private set; }

    public string? SupportReference { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    internal Cp6OutboxDispatchMessage ToDispatchMessage() => new(
        Id,
        MessageId,
        TenantId,
        TopicName,
        PartitionKey,
        Payload,
        CorrelationId,
        CausationId,
        AggregateId,
        AggregateVersion,
        AttemptCount);

    internal void MarkPublished(DateTimeOffset publishedAtUtc)
    {
        Status = Cp6OutboxStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        DeadLetteredAtUtc = null;
        LeaseOwner = null;
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
        LastErrorCode = null;
        SupportReference = null;
    }

    internal void ScheduleRetry(DateTimeOffset availableAtUtc, string errorCode, string? supportReference)
    {
        Status = Cp6OutboxStatus.Pending;
        AvailableAtUtc = availableAtUtc;
        LeaseOwner = null;
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
        LastErrorCode = errorCode;
        SupportReference = supportReference;
    }

    internal void DeadLetter(DateTimeOffset deadLetteredAtUtc, string errorCode, string? supportReference)
    {
        Status = Cp6OutboxStatus.DeadLettered;
        DeadLetteredAtUtc = deadLetteredAtUtc;
        LeaseOwner = null;
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
        LastErrorCode = errorCode;
        SupportReference = supportReference;
    }

    internal void Requeue(DateTimeOffset availableAtUtc)
    {
        if (Status != Cp6OutboxStatus.DeadLettered)
        {
            throw new InvalidOperationException("Only a dead-lettered Outbox message may be requeued.");
        }

        Status = Cp6OutboxStatus.Pending;
        AttemptCount = 0;
        AvailableAtUtc = availableAtUtc;
        DeadLetteredAtUtc = null;
        LastErrorCode = null;
        SupportReference = null;
    }
}

public sealed class Cp6InboxMessage
{
    private Cp6InboxMessage()
    {
    }

    internal Cp6InboxMessage(Cp6InboxDelivery delivery, string payloadSha256, DateTimeOffset receivedAtUtc)
    {
        Id = Guid.NewGuid();
        ConsumerName = delivery.ConsumerName;
        MessageId = delivery.MessageId;
        TenantId = delivery.TenantId;
        TopicName = delivery.TopicName;
        PartitionKey = delivery.PartitionKey;
        PayloadSha256 = payloadSha256;
        AggregateId = delivery.AggregateId;
        AggregateVersion = delivery.AggregateVersion;
        Status = Cp6InboxStatus.Processing;
        ReceivedAtUtc = receivedAtUtc;
    }

    public Guid Id { get; private set; }

    public string ConsumerName { get; private set; } = string.Empty;

    public string MessageId { get; private set; } = string.Empty;

    public Guid TenantId { get; private set; }

    public string TopicName { get; private set; } = string.Empty;

    public string PartitionKey { get; private set; } = string.Empty;

    public string PayloadSha256 { get; private set; } = string.Empty;

    public string AggregateId { get; private set; } = string.Empty;

    public int AggregateVersion { get; private set; }

    public Cp6InboxStatus Status { get; private set; }

    public int AttemptCount { get; private set; } = 1;

    public string? OutcomeCode { get; private set; }

    public string? LastErrorCode { get; private set; }

    public string? SupportReference { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    internal void Complete(DateTimeOffset processedAtUtc, string? outcomeCode = null)
    {
        Status = Cp6InboxStatus.Processed;
        ProcessedAtUtc = processedAtUtc;
        OutcomeCode = outcomeCode;
    }

    internal void BeginRetry()
    {
        if (Status != Cp6InboxStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing Inbox message may be retried.");
        }

        AttemptCount++;
    }

    internal void RecordFailure(string errorCode, string? supportReference)
    {
        LastErrorCode = errorCode;
        SupportReference = supportReference;
    }

    internal void DeadLetter(DateTimeOffset processedAtUtc, string errorCode, string? supportReference)
    {
        RecordFailure(errorCode, supportReference);
        Complete(processedAtUtc, Cp6InboxProcessingResult.DeadLetteredErrorCode);
    }

    internal void Requeue()
    {
        if (Status != Cp6InboxStatus.Processed || OutcomeCode != Cp6InboxProcessingResult.DeadLetteredErrorCode)
        {
            throw new InvalidOperationException("Only a dead-lettered Inbox message may be requeued.");
        }

        Status = Cp6InboxStatus.Processing;
        AttemptCount = 0;
        OutcomeCode = null;
        LastErrorCode = null;
        SupportReference = null;
        ProcessedAtUtc = null;
    }
}

public sealed class Cp6InboxAggregateCheckpoint
{
    private Cp6InboxAggregateCheckpoint()
    {
    }

    internal Cp6InboxAggregateCheckpoint(Cp6InboxDelivery delivery, DateTimeOffset updatedAtUtc)
    {
        Id = Guid.NewGuid();
        ConsumerName = delivery.ConsumerName;
        TenantId = delivery.TenantId;
        AggregateId = delivery.AggregateId;
        AggregateVersion = delivery.AggregateVersion;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public string ConsumerName { get; private set; } = string.Empty;

    public Guid TenantId { get; private set; }

    public string AggregateId { get; private set; } = string.Empty;

    public int AggregateVersion { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    internal void Advance(int aggregateVersion, DateTimeOffset updatedAtUtc)
    {
        if (aggregateVersion <= AggregateVersion)
        {
            throw new InvalidOperationException("The aggregate checkpoint may only advance.");
        }

        AggregateVersion = aggregateVersion;
        UpdatedAtUtc = updatedAtUtc;
    }
}

public sealed class Cp6DeadLetterRecord
{
    private Cp6DeadLetterRecord()
    {
    }

    internal Cp6DeadLetterRecord(
        Cp6DeadLetterDirection direction,
        Guid tenantId,
        string messageId,
        string? consumerName,
        string payloadSha256,
        string errorCode,
        string? supportReference,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        Direction = direction;
        TenantId = tenantId;
        MessageId = messageId;
        ConsumerName = consumerName;
        PayloadSha256 = payloadSha256;
        ErrorCode = errorCode;
        SupportReference = supportReference;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Cp6DeadLetterDirection Direction { get; private set; }

    public Guid TenantId { get; private set; }

    public string MessageId { get; private set; } = string.Empty;

    public string? ConsumerName { get; private set; }

    public string PayloadSha256 { get; private set; } = string.Empty;

    public string ErrorCode { get; private set; } = string.Empty;

    public string? SupportReference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ReplayedAtUtc { get; private set; }

    public string? ReplayReasonCode { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void RecordReplay(string replayReasonCode, DateTimeOffset replayedAtUtc)
    {
        Cp6TransactionalMessagingGuard.ContentSafeCode(replayReasonCode, nameof(replayReasonCode));
        if (ReplayedAtUtc is not null)
        {
            throw new InvalidOperationException("A dead-letter replay may only be recorded once.");
        }

        ReplayReasonCode = replayReasonCode;
        ReplayedAtUtc = replayedAtUtc;
    }
}
