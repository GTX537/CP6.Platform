namespace CP6.Platform.Contracts.ObservabilityV1;

public enum MetricCommitPhaseV1 { BeforeCommit, AfterCommit, ObserverOnly }
public enum MetricKindV1 { Counter, Histogram }
public enum ReceiptPointV1 { ProducerCommitted, ConsumerApplied }
public enum LatencyStatusV1 { NotMeasured, Measured, ClockUnproven, ClockSkew }

public sealed record MetricDefinitionV1(string Name, string Unit, MetricKindV1 Kind, string MeasurementPoint,
    MetricCommitPhaseV1 CommitPhase, string Scope, string Aggregation, string Owner, string Source)
{
    public string ContractVersion => "1.0.0";
    public string Reset => "process-restart";
    public string AllowedValues => "TelemetryPolicyV1 finite registry";
    public static IReadOnlyList<MetricDefinitionV1> Technical { get; } = Array.AsReadOnly(new[]
    {
        new MetricDefinitionV1("cp6.messaging.published", "{attempt}", MetricKindV1.Counter, "transport-return", MetricCommitPhaseV1.ObserverOnly, "attempt", "sum", "transport", "CP6.Platform.Messaging"),
        new MetricDefinitionV1("cp6.messaging.consumed", "{attempt}", MetricKindV1.Counter, "delivery-validation", MetricCommitPhaseV1.ObserverOnly, "attempt", "sum", "transport", "CP6.Platform.Messaging"),
        new MetricDefinitionV1("cp6.messaging.rejected", "{attempt}", MetricKindV1.Counter, "delivery-rejection", MetricCommitPhaseV1.ObserverOnly, "attempt", "sum", "transport", "CP6.Platform.Messaging"),
        new MetricDefinitionV1("cp6.messaging.trace_context.rejected", "{attempt}", MetricKindV1.Counter, "trace-context-validation", MetricCommitPhaseV1.ObserverOnly, "attempt", "sum", "transport", "CP6.Platform.Messaging"),
        new MetricDefinitionV1("cp6.messaging.duration", "ms", MetricKindV1.Histogram, "transport-operation-return", MetricCommitPhaseV1.ObserverOnly, "attempt", "histogram", "transport", "CP6.Platform.Messaging"),
        new MetricDefinitionV1("cp6.outbox.operations", "{attempt}", MetricKindV1.Counter, "enqueue-may-precede-caller-commit", MetricCommitPhaseV1.BeforeCommit, "attempt", "sum", "outbox", "CP6.Platform.EntityFramework"),
        new MetricDefinitionV1("cp6.inbox.operations", "{attempt}", MetricKindV1.Counter, "inbox-operation", MetricCommitPhaseV1.ObserverOnly, "attempt", "sum", "inbox", "CP6.Platform.EntityFramework"),
        new MetricDefinitionV1("cp6.entityframework.duration", "ms", MetricKindV1.Histogram, "operation-return", MetricCommitPhaseV1.ObserverOnly, "attempt", "histogram", "persistence", "CP6.Platform.EntityFramework"),
        new MetricDefinitionV1("cp6.outbox.oldest_available.age", "ms", MetricKindV1.Histogram, "available-row-observation", MetricCommitPhaseV1.ObserverOnly, "attempt", "histogram", "outbox", "CP6.Platform.EntityFramework"),
        new MetricDefinitionV1("cp6.messaging.attempts", "{attempt}", MetricKindV1.Histogram, "row-attempt-observation", MetricCommitPhaseV1.ObserverOnly, "attempt", "histogram", "outbox", "CP6.Platform.EntityFramework"),
        new MetricDefinitionV1("cp6.http.duration", "ms", MetricKindV1.Histogram, "request-return", MetricCommitPhaseV1.ObserverOnly, "attempt", "histogram", "http", "CP6.Platform.AspNetCore")
    });
    public static MetricDefinitionV1 OwnerLatency { get; } = new("cp6.owner.committed_to_applied", "s", MetricKindV1.Histogram,
        "producer-owner-committed-to-consumer-owner-applied", MetricCommitPhaseV1.AfterCommit, "business", "histogram", "receipt-owners", "owner-receipts");
}

/// <summary>Explicit receipt input, not inferred from technical transport counters. IDs never become telemetry tags.</summary>
public sealed record OwnerReceiptV1(string MessageRef, string OwnerRef, ReceiptPointV1 Point, DateTimeOffset AtUtc,
    string ClockProvenance, bool IsRetryOrDuplicate);

public sealed record OwnerReceiptLatencyV1(LatencyStatusV1 Status, double? Seconds)
{
    public static OwnerReceiptLatencyV1 Measure(OwnerReceiptV1? producer, OwnerReceiptV1? consumer)
    {
        if (producer is null || consumer is null || producer.Point != ReceiptPointV1.ProducerCommitted || consumer.Point != ReceiptPointV1.ConsumerApplied ||
            producer.MessageRef != consumer.MessageRef || producer.OwnerRef == consumer.OwnerRef || producer.IsRetryOrDuplicate || consumer.IsRetryOrDuplicate ||
            !ValidId(producer.MessageRef) || !ValidId(producer.OwnerRef) || !ValidId(consumer.OwnerRef)) return new(LatencyStatusV1.NotMeasured, null);
        if (!ValidId(producer.ClockProvenance) || producer.ClockProvenance != consumer.ClockProvenance || producer.AtUtc.Offset != TimeSpan.Zero || consumer.AtUtc.Offset != TimeSpan.Zero || producer.AtUtc <= DateTimeOffset.UnixEpoch)
            return new(LatencyStatusV1.ClockUnproven, null);
        var seconds = (consumer.AtUtc - producer.AtUtc).TotalSeconds;
        return seconds < 0 ? new(LatencyStatusV1.ClockSkew, null) : new(LatencyStatusV1.Measured, seconds);
    }
    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && !value.Any(char.IsControl);
}
