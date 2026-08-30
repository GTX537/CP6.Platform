using CloudNative.CloudEvents;

namespace CP6.Platform.Messaging;

public sealed record Cp6DaprPublishReceipt(string PubSubName, string TopicName, string PartitionKey, string EventId);

/// <summary>
/// Publishes an already-structured CP6 CloudEvent only after the P04 contract
/// and the P05 Kafka addressing profile both pass.
/// </summary>
public sealed class Cp6DaprEventPublisher
{
    private readonly ICp6DaprTransport transport;
    private readonly Cp6CloudEventValidator validator;

    public Cp6DaprEventPublisher(ICp6DaprTransport transport, Cp6CloudEventValidator validator)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<Cp6DaprPublishReceipt> PublishAsync(
        ReadOnlyMemory<byte> structuredEvent,
        CancellationToken cancellationToken = default)
    {
        var result = validator.Validate(structuredEvent);
        if (!result.IsValid || result.CloudEvent is null)
        {
            throw new Cp6DaprContractException(Cp6DaprContractFailure.EventContractInvalid);
        }

        var cloudEvent = result.CloudEvent;
        var identity = Cp6EventContractIdentity.Parse(cloudEvent.Type!);
        var topicName = Cp6DaprKafkaConventions.GetTopic(identity);
        var partitionKey = GetExpectedPartitionKey(cloudEvent);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Cp6DaprKafkaConventions.PartitionKeyMetadata] = partitionKey
        };

        using var operation = Cp6MessagingTelemetry.StartPublish();
        try
        {
            await transport.PublishAsync(
                Cp6DaprKafkaConventions.PubSubName,
                topicName,
                structuredEvent,
                Cp6CloudEventCodec.StructuredContentType,
                metadata,
                cancellationToken);
            operation.Success(
                "published",
                Cp6MessagingTelemetry.MeasurementKind.Published);
        }
        catch (OperationCanceledException)
        {
            operation.Cancelled();
            throw;
        }
        catch (Exception)
        {
            operation.Failure("transport_failure");
            throw;
        }

        return new Cp6DaprPublishReceipt(
            Cp6DaprKafkaConventions.PubSubName,
            topicName,
            partitionKey,
            cloudEvent.Id!);
    }

    internal static string GetExpectedPartitionKey(CloudEvent cloudEvent)
    {
        var tenantValue = (string?)cloudEvent[Cp6CloudEventAttributes.TenantId];
        var aggregateId = (string?)cloudEvent[Cp6CloudEventAttributes.AggregateId];
        if (!Guid.TryParseExact(tenantValue, "D", out var tenantId) || aggregateId is null)
        {
            throw new Cp6DaprContractException(Cp6DaprContractFailure.EventContractInvalid);
        }

        return Cp6DaprKafkaConventions.GetPartitionKey(tenantId, aggregateId);
    }
}
