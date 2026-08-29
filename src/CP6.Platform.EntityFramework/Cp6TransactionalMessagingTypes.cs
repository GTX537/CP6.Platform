using System.Security.Cryptography;

namespace CP6.Platform.EntityFramework;

public enum Cp6OutboxStatus
{
    Pending = 0,
    Dispatching = 1,
    Published = 2,
    DeadLettered = 3
}

public enum Cp6InboxStatus
{
    Processing = 0,
    Processed = 1
}

public enum Cp6InboxDisposition
{
    Applied = 0,
    Duplicate = 1,
    IgnoredOutOfOrder = 2,
    PayloadConflict = 3,
    Invalid = 4,
    RetryScheduled = 5,
    DeadLettered = 6
}

public enum Cp6DeadLetterDirection
{
    Outbound = 0,
    Inbound = 1
}

public sealed record Cp6MessageValidationResult(bool IsValid, string ErrorCode)
{
    public const string InvalidErrorCode = "CP6_PERSISTED_MESSAGE_INVALID";

    public static Cp6MessageValidationResult Valid { get; } = new(true, string.Empty);

    public static Cp6MessageValidationResult Invalid(string errorCode = InvalidErrorCode)
    {
        Cp6TransactionalMessagingGuard.ContentSafeCode(errorCode, nameof(errorCode));
        return new(false, errorCode);
    }
}

public sealed record Cp6OutboxEnvelope(
    string MessageId,
    Guid TenantId,
    string TopicName,
    string PartitionKey,
    ReadOnlyMemory<byte> Payload,
    string CorrelationId,
    string CausationId,
    string AggregateId,
    int AggregateVersion);

public sealed record Cp6InboxDelivery(
    string ConsumerName,
    string MessageId,
    Guid TenantId,
    string TopicName,
    string PartitionKey,
    ReadOnlyMemory<byte> Payload,
    string AggregateId,
    int AggregateVersion);

public sealed record Cp6OutboxDispatchMessage(
    Guid Id,
    string MessageId,
    Guid TenantId,
    string TopicName,
    string PartitionKey,
    ReadOnlyMemory<byte> Payload,
    string CorrelationId,
    string CausationId,
    string AggregateId,
    int AggregateVersion,
    int AttemptCount);

public sealed record Cp6ClaimedOutboxMessage(
    Cp6OutboxDispatchMessage Message,
    string LeaseOwner,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc);

public sealed record Cp6InboxProcessingResult(
    Cp6InboxDisposition Disposition,
    string ErrorCode,
    string PayloadSha256)
{
    public const string PayloadConflictErrorCode = "CP6_INBOX_PAYLOAD_CONFLICT";
    public const string OutOfOrderErrorCode = "CP6_INBOX_OUT_OF_ORDER";
    public const string DeadLetteredErrorCode = "CP6_INBOX_DEAD_LETTERED";

    public bool Applied => Disposition == Cp6InboxDisposition.Applied;
}

public sealed record Cp6OutboxDispatchResult(
    int Claimed,
    int Published,
    int RetryScheduled,
    int DeadLettered);

public sealed record Cp6RetentionResult(
    int OutboxDeleted,
    int InboxDeleted,
    int DeadLettersDeleted);

public interface ICp6OutboxEnvelopeValidator
{
    Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope);
}

public interface ICp6InboxDeliveryValidator
{
    Cp6MessageValidationResult Validate(Cp6InboxDelivery delivery);
}

public interface ICp6OutboxPublisher
{
    Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default);
}

public sealed class Cp6OutboxPublishException : Exception
{
    public Cp6OutboxPublishException(string errorCode, bool retryable, string? supportReference = null, Exception? innerException = null)
        : base("The CP6 Outbox publisher reported a content-safe failure.", innerException)
    {
        Cp6TransactionalMessagingGuard.ContentSafeCode(errorCode, nameof(errorCode));
        if (supportReference is not null)
        {
            Cp6TransactionalMessagingGuard.Identifier(supportReference, nameof(supportReference), 128);
        }

        ErrorCode = errorCode;
        Retryable = retryable;
        SupportReference = supportReference;
    }

    public string ErrorCode { get; }

    public bool Retryable { get; }

    public string? SupportReference { get; }
}

public sealed class Cp6InboxProcessingException : Exception
{
    public Cp6InboxProcessingException(string errorCode, bool retryable, string? supportReference = null, Exception? innerException = null)
        : base("The CP6 Inbox database handler reported a content-safe failure.", innerException)
    {
        Cp6TransactionalMessagingGuard.ContentSafeCode(errorCode, nameof(errorCode));
        if (supportReference is not null)
        {
            Cp6TransactionalMessagingGuard.Identifier(supportReference, nameof(supportReference), 128);
        }

        ErrorCode = errorCode;
        Retryable = retryable;
        SupportReference = supportReference;
    }

    public string ErrorCode { get; }

    public bool Retryable { get; }

    public string? SupportReference { get; }
}

public sealed class Cp6TransactionalMessagingOptions
{
    public TimeSpan OutboxLeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    public int DispatchBatchSize { get; init; } = 100;

    public int MaxOutboxAttempts { get; init; } = 10;

    public int MaxInboxAttempts { get; init; } = 10;

    public TimeSpan InitialOutboxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumOutboxRetryDelay { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan PublishedOutboxRetention { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan ProcessedInboxRetention { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan DeadLetterRetention { get; init; } = TimeSpan.FromDays(90);

    public void Validate()
    {
        if (OutboxLeaseDuration <= TimeSpan.Zero || OutboxLeaseDuration > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(OutboxLeaseDuration));
        }

        if (DispatchBatchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(DispatchBatchSize));
        }

        if (MaxOutboxAttempts is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOutboxAttempts));
        }

        if (MaxInboxAttempts is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInboxAttempts));
        }

        if (InitialOutboxRetryDelay <= TimeSpan.Zero || InitialOutboxRetryDelay > MaximumOutboxRetryDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialOutboxRetryDelay));
        }

        if (MaximumOutboxRetryDelay <= TimeSpan.Zero || MaximumOutboxRetryDelay > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumOutboxRetryDelay));
        }

        ValidateRetention(PublishedOutboxRetention, nameof(PublishedOutboxRetention));
        ValidateRetention(ProcessedInboxRetention, nameof(ProcessedInboxRetention));
        ValidateRetention(DeadLetterRetention, nameof(DeadLetterRetention));
    }

    private static void ValidateRetention(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(3650))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    internal TimeSpan RetryDelay(int attemptCount)
    {
        var exponent = Math.Min(Math.Max(attemptCount - 1, 0), 30);
        var ticks = InitialOutboxRetryDelay.Ticks * Math.Pow(2, exponent);
        return TimeSpan.FromTicks((long)Math.Min(ticks, MaximumOutboxRetryDelay.Ticks));
    }
}

internal static class Cp6TransactionalMessagingGuard
{
    public const int MaxPayloadBytes = 1_048_576;

    public static void Envelope(Cp6OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Message(envelope.MessageId, envelope.TenantId, envelope.TopicName, envelope.PartitionKey, envelope.Payload, envelope.AggregateId, envelope.AggregateVersion);
        Identifier(envelope.CorrelationId, nameof(envelope.CorrelationId), 128);
        Identifier(envelope.CausationId, nameof(envelope.CausationId), 128);
    }

    public static void Delivery(Cp6InboxDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        Identifier(delivery.ConsumerName, nameof(delivery.ConsumerName), 128);
        Message(delivery.MessageId, delivery.TenantId, delivery.TopicName, delivery.PartitionKey, delivery.Payload, delivery.AggregateId, delivery.AggregateVersion);
    }

    public static string Sha256(ReadOnlyMemory<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant();

    public static void Identifier(string value, string name, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maxLength || value.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    public static void ContentSafeCode(string value, string name)
    {
        Identifier(value, name, 128);
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
        {
            throw new ArgumentException("Error codes may contain only ASCII letters, digits, dot, dash, and underscore.", name);
        }
    }

    private static void Message(
        string messageId,
        Guid tenantId,
        string topicName,
        string partitionKey,
        ReadOnlyMemory<byte> payload,
        string aggregateId,
        int aggregateVersion)
    {
        Identifier(messageId, nameof(messageId), 128);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenant id is required.", nameof(tenantId));
        }

        Identifier(topicName, nameof(topicName), 249);
        Identifier(partitionKey, nameof(partitionKey), 512);
        Identifier(aggregateId, nameof(aggregateId), 128);
        if (payload.IsEmpty || payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        if (aggregateVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(aggregateVersion));
        }
    }
}
