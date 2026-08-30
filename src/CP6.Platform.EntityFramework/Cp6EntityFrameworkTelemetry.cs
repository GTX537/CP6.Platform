using System.Diagnostics;
using System.Diagnostics.Metrics;
using CP6.Platform.Abstractions;

namespace CP6.Platform.EntityFramework;

internal static class Cp6EntityFrameworkTelemetry
{
    private static readonly ActivitySource ActivitySource = new(Cp6TelemetrySources.EntityFramework);
    private static readonly Meter Meter = new(Cp6TelemetryMeters.EntityFramework);
    private static readonly Counter<long> OutboxOperations = Meter.CreateCounter<long>("cp6.outbox.operations");
    private static readonly Counter<long> InboxOperations = Meter.CreateCounter<long>("cp6.inbox.operations");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "cp6.entityframework.duration",
        unit: "ms");
    private static readonly Histogram<double> OldestAvailableAge = Meter.CreateHistogram<double>(
        "cp6.outbox.oldest_available.age",
        unit: "ms");
    private static readonly Histogram<long> Attempts = Meter.CreateHistogram<long>("cp6.messaging.attempts");

    internal static OperationScope StartOutbox(ActivityKind kind = ActivityKind.Internal) => new(
        Cp6TelemetryConventions.OutboxDispatchOperation,
        kind);

    internal static OperationScope StartInbox(ActivityKind kind = ActivityKind.Consumer) => new(
        Cp6TelemetryConventions.InboxProcessOperation,
        kind);

    internal static void RecordOldestAvailableAge(TimeSpan age)
    {
        var tags = Tags(Cp6TelemetryConventions.OutboxDispatchOperation, "success", null, "claim");
        TryRecord(() => OldestAvailableAge.Record(Math.Max(0, age.TotalMilliseconds), tags));
    }

    internal static void RecordAttemptCount(int attemptCount, string operation, string disposition)
    {
        EnsureOperation(operation);
        EnsureDisposition(disposition);
        var tags = Tags(operation, "success", null, disposition);
        TryRecord(() => Attempts.Record(Math.Max(0, attemptCount), tags));
    }

    internal sealed class OperationScope : IDisposable
    {
        private readonly string operation;
        private readonly long startedAt;
        private readonly Activity? activity;
        private bool completed;

        internal OperationScope(string operation, ActivityKind kind)
        {
            EnsureOperation(operation);
            this.operation = operation;
            startedAt = Stopwatch.GetTimestamp();
            activity = TryStartActivity(operation, kind);
            TryRecord(() => activity?.SetTag(Cp6TelemetryConventions.OperationTag, operation));
        }

        internal void Success(string disposition) =>
            Complete("success", null, disposition, ActivityStatusCode.Ok);

        internal void Rejected(string errorCode, string disposition) =>
            Complete("rejected", errorCode, disposition, ActivityStatusCode.Error);

        internal void Failure(string errorCode, string disposition) =>
            Complete("failure", errorCode, disposition, ActivityStatusCode.Error);

        internal void Cancelled(string disposition) =>
            Complete("cancelled", "cancelled", disposition, ActivityStatusCode.Error);

        public void Dispose()
        {
            if (!completed)
            {
                Complete("failure", "operation_incomplete", "failed", ActivityStatusCode.Error);
            }

            TryRecord(() => activity?.Dispose());
        }

        private void Complete(
            string outcome,
            string? errorCode,
            string disposition,
            ActivityStatusCode status)
        {
            if (completed)
            {
                return;
            }

            EnsureOutcome(outcome);
            EnsureErrorCode(errorCode);
            EnsureDisposition(disposition);
            completed = true;
            var tags = Tags(operation, outcome, errorCode, disposition);
            TryRecord(() =>
            {
                activity?.SetTag(Cp6TelemetryConventions.OutcomeTag, outcome);
                activity?.SetTag(Cp6TelemetryConventions.MessagingDispositionTag, disposition);
                if (errorCode is not null)
                {
                    activity?.SetTag(Cp6TelemetryConventions.ErrorCodeTag, errorCode);
                }

                activity?.SetStatus(status);
                Counter(operation).Add(1, tags);
                Duration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
            });
        }
    }

    private static Activity? TryStartActivity(string operation, ActivityKind kind)
    {
        try
        {
            return ActivitySource.StartActivity(operation, kind);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryRecord(Action record)
    {
        try
        {
            record();
        }
        catch (Exception)
        {
            // Telemetry is observer-only and cannot change transactional behavior.
        }
    }

    private static Counter<long> Counter(string operation) => operation switch
    {
        Cp6TelemetryConventions.OutboxDispatchOperation => OutboxOperations,
        Cp6TelemetryConventions.InboxProcessOperation => InboxOperations,
        _ => throw new ArgumentException("Entity Framework operation is not in the stable allowlist.", nameof(operation))
    };

    private static KeyValuePair<string, object?>[] Tags(
        string operation,
        string outcome,
        string? errorCode,
        string disposition)
    {
        var tags = new List<KeyValuePair<string, object?>>(4)
        {
            new(Cp6TelemetryConventions.OperationTag, operation),
            new(Cp6TelemetryConventions.OutcomeTag, outcome),
            new(Cp6TelemetryConventions.MessagingDispositionTag, disposition)
        };
        if (errorCode is not null)
        {
            tags.Add(new(Cp6TelemetryConventions.ErrorCodeTag, errorCode));
        }

        return tags.ToArray();
    }

    private static void EnsureOperation(string operation)
    {
        if (operation is not (
                Cp6TelemetryConventions.OutboxDispatchOperation or
                Cp6TelemetryConventions.InboxProcessOperation))
        {
            throw new ArgumentException("Entity Framework operation is not in the stable allowlist.", nameof(operation));
        }
    }

    private static void EnsureOutcome(string outcome)
    {
        if (outcome is not ("success" or "rejected" or "failure" or "cancelled"))
        {
            throw new ArgumentException("Entity Framework outcome is not in the stable allowlist.", nameof(outcome));
        }
    }

    private static void EnsureErrorCode(string? errorCode)
    {
        if (errorCode is not null and not (
                "validation_failed" or
                "publish_failure" or
                "processing_failure" or
                "payload_conflict" or
                "out_of_order" or
                "cancelled" or
                "operation_incomplete"))
        {
            throw new ArgumentException("Entity Framework error code is not in the stable allowlist.", nameof(errorCode));
        }
    }

    private static void EnsureDisposition(string disposition)
    {
        if (disposition is not (
                "enqueue" or
                "claim" or
                "published" or
                "invalid" or
                "duplicate" or
                "payload_conflict" or
                "applied" or
                "ignored_out_of_order" or
                "retry_scheduled" or
                "dead_lettered" or
                "replayed" or
                "retained" or
                "cancelled" or
                "failed"))
        {
            throw new ArgumentException("Entity Framework disposition is not in the stable allowlist.", nameof(disposition));
        }
    }
}
