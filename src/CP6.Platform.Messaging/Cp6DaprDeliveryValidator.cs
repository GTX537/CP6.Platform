using System.Diagnostics;
using CloudNative.CloudEvents;

namespace CP6.Platform.Messaging;

public enum Cp6DaprContractFailure
{
    None,
    EventContractInvalid,
    TopicMismatch,
    PartitionKeyMismatch
}

public sealed class Cp6DaprContractException : InvalidOperationException
{
    public Cp6DaprContractException(Cp6DaprContractFailure failure)
        : base($"CP6 Dapr/Kafka contract failed: {failure}.")
    {
        Failure = failure;
    }

    public Cp6DaprContractFailure Failure { get; }
}

public sealed record Cp6DaprDeliveryValidationResult(
    bool IsValid,
    Cp6DaprContractFailure Failure,
    CloudEvent? CloudEvent)
{
    public const string ErrorCode = "CP6_DAPR_MESSAGE_INVALID";

    public ActivityContext? ParentContext { get; init; }
}

/// <summary>
/// Validates the P04 event before comparing broker-owned topic and partition
/// metadata. No handler should run until this result is valid.
/// </summary>
public sealed class Cp6DaprDeliveryValidator
{
    private readonly Cp6CloudEventValidator validator;

    public Cp6DaprDeliveryValidator(Cp6CloudEventValidator validator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public Cp6DaprDeliveryValidationResult Validate(
        ReadOnlyMemory<byte> structuredEvent,
        string topicName,
        string partitionKey)
    {
        var parentContext = Cp6TraceContextCodec.TryExtract(structuredEvent);
        var eventResult = validator.Validate(structuredEvent);
        if (!eventResult.IsValid || eventResult.CloudEvent is null)
        {
            using var operation = Cp6MessagingTelemetry.StartConsume(parentContext: null);
            operation.Rejected("event_contract_invalid");
            return new(false, Cp6DaprContractFailure.EventContractInvalid, null);
        }

        var cloudEvent = eventResult.CloudEvent;
        var expectedTopic = Cp6DaprKafkaConventions.GetTopic(Cp6EventContractIdentity.Parse(cloudEvent.Type!));
        if (!string.Equals(topicName, expectedTopic, StringComparison.Ordinal))
        {
            using var operation = Cp6MessagingTelemetry.StartConsume(parentContext: null);
            operation.Rejected("topic_mismatch");
            return new(false, Cp6DaprContractFailure.TopicMismatch, null);
        }

        var expectedPartitionKey = Cp6DaprEventPublisher.GetExpectedPartitionKey(cloudEvent);
        if (!string.Equals(partitionKey, expectedPartitionKey, StringComparison.Ordinal))
        {
            using var operation = Cp6MessagingTelemetry.StartConsume(parentContext: null);
            operation.Rejected("partition_key_mismatch");
            return new(false, Cp6DaprContractFailure.PartitionKeyMismatch, null);
        }

        using (var operation = Cp6MessagingTelemetry.StartConsume(parentContext))
        {
            operation.Success("consumed", Cp6MessagingTelemetry.MeasurementKind.Consumed);
        }

        return new(true, Cp6DaprContractFailure.None, cloudEvent)
        {
            ParentContext = parentContext
        };
    }
}
