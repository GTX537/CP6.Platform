namespace CP6.Platform.Messaging;

/// <summary>
/// Carries the required, non-payload metadata for a CP6 structured CloudEvent.
/// </summary>
public sealed record Cp6CloudEventDescriptor(
    string Id,
    Uri Source,
    string Type,
    string Subject,
    DateTimeOffset Time,
    Uri DataSchema,
    Guid TenantId,
    string CorrelationId,
    string CausationId,
    string AggregateId,
    int AggregateVersion,
    string SchemaVersion,
    string Region);
