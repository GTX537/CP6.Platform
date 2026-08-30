using System.Diagnostics;
using System.Diagnostics.Metrics;
using CP6.Platform.Abstractions;
using CP6.Platform.EntityFramework;

namespace CP6.Platform.UnitTests;

[Collection(nameof(TransactionalMessagingTelemetryCollection))]
public sealed class TransactionalMessagingTelemetryTests
{
    [Fact]
    public void OutboxAndInbox_EmitStableActivitiesAndMetricsForEveryDisposition()
    {
        using var capture = new TelemetryCapture();

        foreach (var disposition in new[] { "enqueue", "claim", "published", "retry_scheduled", "dead_lettered", "replayed", "retained" })
        {
            using var operation = Cp6EntityFrameworkTelemetry.StartOutbox(ActivityKind.Producer);
            operation.Success(disposition);
        }

        foreach (var disposition in new[] { "invalid", "duplicate", "payload_conflict", "applied", "ignored_out_of_order", "retry_scheduled", "dead_lettered", "replayed", "retained" })
        {
            using var operation = Cp6EntityFrameworkTelemetry.StartInbox(ActivityKind.Consumer);
            operation.Success(disposition);
        }

        Assert.Equal(16, capture.Activities.Count);
        Assert.All(capture.Activities, activity =>
        {
            Assert.Contains(activity.OperationName, new[]
            {
                Cp6TelemetryConventions.OutboxDispatchOperation,
                Cp6TelemetryConventions.InboxProcessOperation
            });
            Assert.Equal(activity.OperationName, activity.GetTagItem(Cp6TelemetryConventions.OperationTag));
            Assert.Equal("success", activity.GetTagItem(Cp6TelemetryConventions.OutcomeTag));
            Assert.All(activity.TagObjects, tag => Assert.Contains(tag.Key, Cp6TelemetryConventions.AllowedMetricTags));
        });

        Assert.Equal(7, capture.Measurements.Count(measurement => measurement.InstrumentName == "cp6.outbox.operations"));
        Assert.Equal(9, capture.Measurements.Count(measurement => measurement.InstrumentName == "cp6.inbox.operations"));
        Assert.Contains(capture.Measurements, measurement => measurement.InstrumentName == "cp6.entityframework.duration");
        Assert.All(capture.Measurements, measurement =>
            Assert.All(measurement.Tags, tag => Assert.Contains(tag.Key, Cp6TelemetryConventions.AllowedMetricTags)));
    }

    [Fact]
    public void AgeAndAttempts_AreMeasurementsAndNeverLabels()
    {
        using var capture = new TelemetryCapture();

        Cp6EntityFrameworkTelemetry.RecordOldestAvailableAge(TimeSpan.FromSeconds(17));
        Cp6EntityFrameworkTelemetry.RecordAttemptCount(3, Cp6TelemetryConventions.OutboxDispatchOperation, "claim");

        var age = Assert.Single(capture.Measurements, measurement => measurement.InstrumentName == "cp6.outbox.oldest_available.age");
        var attempts = Assert.Single(capture.Measurements, measurement => measurement.InstrumentName == "cp6.messaging.attempts");
        Assert.Equal(17_000, age.Value);
        Assert.Equal(3, attempts.Value);
        Assert.All(capture.Measurements, measurement =>
        {
            Assert.DoesNotContain(measurement.Tags, tag => tag.Key.Contains("age", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(measurement.Tags, tag => tag.Key.Contains("attempt", StringComparison.OrdinalIgnoreCase));
            Assert.All(measurement.Tags, tag => Assert.Contains(tag.Key, Cp6TelemetryConventions.AllowedMetricTags));
        });
    }

    private sealed class TelemetryCapture : IDisposable
    {
        private readonly ActivityListener activityListener;
        private readonly MeterListener meterListener;

        public TelemetryCapture()
        {
            activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Cp6TelemetrySources.EntityFramework,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Activities.Add(activity)
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
                Measurements.Add(new(instrument.Name, value, tags.ToArray())));
            meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Measurements.Add(new(instrument.Name, value, tags.ToArray())));
            meterListener.Start();
        }

        public List<Activity> Activities { get; } = [];

        public List<Measurement> Measurements { get; } = [];

        public void Dispose()
        {
            meterListener.Dispose();
            activityListener.Dispose();
        }
    }

    private sealed record Measurement(
        string InstrumentName,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}

[CollectionDefinition(nameof(TransactionalMessagingTelemetryCollection), DisableParallelization = true)]
public sealed class TransactionalMessagingTelemetryCollection;
