using System.Diagnostics;
using System.Diagnostics.Metrics;
using CP6.Platform.Abstractions;

namespace CP6.Platform.Messaging;

internal static class Cp6MessagingTelemetry
{
    private const string Transport = "dapr";
    private static readonly ActivitySource ActivitySource = new(Cp6TelemetrySources.Messaging);
    private static readonly Meter Meter = new(Cp6TelemetryMeters.Messaging);
    private static readonly Counter<long> PublishedCounter = Meter.CreateCounter<long>("cp6.messaging.published");
    private static readonly Counter<long> ConsumedCounter = Meter.CreateCounter<long>("cp6.messaging.consumed");
    private static readonly Counter<long> RejectedCounter = Meter.CreateCounter<long>("cp6.messaging.rejected");
    private static readonly Counter<long> TraceContextRejectedCounter = Meter.CreateCounter<long>(
        "cp6.messaging.trace_context.rejected");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "cp6.messaging.duration",
        unit: "ms");

    internal static OperationScope StartPublish() => new(
        Cp6TelemetryConventions.MessagingPublishOperation,
        ActivityKind.Producer,
        parentContext: null);

    internal static OperationScope StartInvoke() => new(
        Cp6TelemetryConventions.DaprInvokeOperation,
        ActivityKind.Client,
        parentContext: null);

    internal static OperationScope StartConsume(ActivityContext? parentContext) => new(
        Cp6TelemetryConventions.MessagingConsumeOperation,
        ActivityKind.Consumer,
        parentContext ?? default(ActivityContext));

    internal static void RecordTraceContextRejected() => TraceContextRejectedCounter.Add(
        1,
        new KeyValuePair<string, object?>(
            Cp6TelemetryConventions.ErrorCodeTag,
            "invalid_trace_context"));

    internal sealed class OperationScope : IDisposable
    {
        private readonly string operation;
        private readonly long startedAt;
        private readonly Activity? activity;
        private bool completed;

        internal OperationScope(string operation, ActivityKind kind, ActivityContext? parentContext)
        {
            EnsureOperation(operation);
            this.operation = operation;
            startedAt = Stopwatch.GetTimestamp();
            activity = parentContext.HasValue
                ? ActivitySource.StartActivity(operation, kind, parentContext.Value)
                : ActivitySource.StartActivity(operation, kind);
            activity?.SetTag(Cp6TelemetryConventions.OperationTag, operation);
            activity?.SetTag(Cp6TelemetryConventions.MessagingTransportTag, Transport);
        }

        internal void Success(string disposition, MeasurementKind measurementKind) =>
            Complete("success", errorCode: null, disposition, measurementKind, ActivityStatusCode.Ok);

        internal void Rejected(string errorCode) =>
            Complete("rejected", errorCode, "drop", MeasurementKind.Rejected, ActivityStatusCode.Error);

        internal void Failure(string errorCode) =>
            Complete("failure", errorCode, "failed", MeasurementKind.None, ActivityStatusCode.Error);

        internal void Cancelled() =>
            Complete("cancelled", "cancelled", "cancelled", MeasurementKind.None, ActivityStatusCode.Error);

        public void Dispose()
        {
            if (!completed)
            {
                Failure("operation_incomplete");
            }

            activity?.Dispose();
        }

        private void Complete(
            string outcome,
            string? errorCode,
            string disposition,
            MeasurementKind measurementKind,
            ActivityStatusCode activityStatus)
        {
            if (completed)
            {
                throw new InvalidOperationException("Messaging telemetry operation is already complete.");
            }

            EnsureOutcome(outcome);
            EnsureErrorCode(errorCode);
            EnsureDisposition(disposition);
            completed = true;
            activity?.SetTag(Cp6TelemetryConventions.OutcomeTag, outcome);
            activity?.SetTag(Cp6TelemetryConventions.MessagingDispositionTag, disposition);
            if (errorCode is not null)
            {
                activity?.SetTag(Cp6TelemetryConventions.ErrorCodeTag, errorCode);
            }

            activity?.SetStatus(activityStatus);
            var tags = Tags(outcome, errorCode, disposition);
            Counter(measurementKind)?.Add(1, tags);
            Duration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        }

        private KeyValuePair<string, object?>[] Tags(
            string outcome,
            string? errorCode,
            string disposition)
        {
            var tags = new List<KeyValuePair<string, object?>>(5)
            {
                new(Cp6TelemetryConventions.OperationTag, operation),
                new(Cp6TelemetryConventions.OutcomeTag, outcome),
                new(Cp6TelemetryConventions.MessagingTransportTag, Transport),
                new(Cp6TelemetryConventions.MessagingDispositionTag, disposition)
            };
            if (errorCode is not null)
            {
                tags.Add(new(Cp6TelemetryConventions.ErrorCodeTag, errorCode));
            }

            return tags.ToArray();
        }
    }

    internal enum MeasurementKind
    {
        None,
        Published,
        Consumed,
        Rejected
    }

    private static Counter<long>? Counter(MeasurementKind kind) => kind switch
    {
        MeasurementKind.None => null,
        MeasurementKind.Published => PublishedCounter,
        MeasurementKind.Consumed => ConsumedCounter,
        MeasurementKind.Rejected => RejectedCounter,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Messaging measurement kind is not supported.")
    };

    private static void EnsureOperation(string operation)
    {
        if (operation != Cp6TelemetryConventions.MessagingPublishOperation &&
            operation != Cp6TelemetryConventions.DaprInvokeOperation &&
            operation != Cp6TelemetryConventions.MessagingConsumeOperation)
        {
            throw new ArgumentException("Messaging operation is not in the stable allowlist.", nameof(operation));
        }
    }

    private static void EnsureOutcome(string outcome)
    {
        if (outcome is not ("success" or "rejected" or "failure" or "cancelled"))
        {
            throw new ArgumentException("Messaging outcome is not in the stable allowlist.", nameof(outcome));
        }
    }

    private static void EnsureErrorCode(string? errorCode)
    {
        if (errorCode is not null and not (
                "event_contract_invalid" or
                "topic_mismatch" or
                "partition_key_mismatch" or
                "transport_failure" or
                "invocation_failure" or
                "cancelled" or
                "operation_incomplete"))
        {
            throw new ArgumentException("Messaging error code is not in the stable allowlist.", nameof(errorCode));
        }
    }

    private static void EnsureDisposition(string disposition)
    {
        if (disposition is not ("published" or "invoked" or "consumed" or "drop" or "failed" or "cancelled"))
        {
            throw new ArgumentException("Messaging disposition is not in the stable allowlist.", nameof(disposition));
        }
    }
}
