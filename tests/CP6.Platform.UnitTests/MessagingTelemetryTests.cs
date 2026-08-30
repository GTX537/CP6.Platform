using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.Messaging;

namespace CP6.Platform.UnitTests;

[Collection(nameof(MessagingTelemetryCollection))]
public sealed class MessagingTelemetryTests
{
    private const string TraceParent = "00-11111111111111111111111111111111-2222222222222222-01";
    private static readonly string ContractRoot = FindContractRoot();

    [Fact]
    public async Task PublishAndInvoke_EmitStableActivitiesAndMetrics()
    {
        using var capture = new TelemetryCapture();
        var transport = new RecordingTransport();
        var publisher = Publisher(transport);
        var invoker = new Cp6DaprServiceInvoker(transport);

        await publisher.PublishAsync(LoadExample("valid"));
        using var response = await invoker.InvokeAsync(
            HttpMethod.Post,
            "cp6-crm-api",
            "api/orders/create");

        var publish = Assert.Single(capture.Activities, activity =>
            activity.OperationName == Cp6TelemetryConventions.MessagingPublishOperation);
        var invoke = Assert.Single(capture.Activities, activity =>
            activity.OperationName == Cp6TelemetryConventions.DaprInvokeOperation);
        Assert.Equal(ActivityKind.Producer, publish.Kind);
        Assert.Equal(ActivityKind.Client, invoke.Kind);
        AssertStableActivity(publish, "success", expectedDisposition: "published");
        AssertStableActivity(invoke, "success", expectedDisposition: "invoked");
        Assert.Contains(capture.Measurements, measurement => measurement.InstrumentName == "cp6.messaging.published");
        Assert.Contains(capture.Measurements, measurement => measurement.InstrumentName == "cp6.messaging.duration");
        Assert.All(capture.Measurements, AssertStableMetricTags);
    }

    [Fact]
    public async Task TransportFailure_RecordsFixedCodeWithoutExceptionOrAddress()
    {
        using var capture = new TelemetryCapture();
        var transport = new RecordingTransport
        {
            PublishException = new HttpRequestException("secret-host.example.invalid must-not-appear")
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => Publisher(transport).PublishAsync(LoadExample("valid")));

        var activity = Assert.Single(capture.Activities);
        AssertStableActivity(activity, "failure", expectedDisposition: "failed");
        Assert.Equal("transport_failure", activity.GetTagItem(Cp6TelemetryConventions.ErrorCodeTag));
        Assert.DoesNotContain("secret-host", RenderActivity(activity), StringComparison.OrdinalIgnoreCase);
        Assert.All(capture.Measurements, AssertStableMetricTags);
        Assert.DoesNotContain("secret-host", JsonSerializer.Serialize(capture.Measurements), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Consumer_UsesRemoteParentOrCreatesRootWhenTelemetryIsInvalid()
    {
        using var capture = new TelemetryCapture();
        var validator = DeliveryValidator();
        var validParentBody = AddTelemetry(LoadExample("valid"), ("traceparent", TraceParent));
        var invalidParentBody = AddTelemetry(LoadExample("valid"), ("traceparent", "invalid-parent-secret"));

        var withParent = validator.Validate(validParentBody, Topic(), PartitionKey());
        var root = validator.Validate(invalidParentBody, Topic(), PartitionKey());

        Assert.True(withParent.IsValid);
        Assert.True(root.IsValid);
        var consumeActivities = capture.Activities
            .Where(activity => activity.OperationName == Cp6TelemetryConventions.MessagingConsumeOperation)
            .ToArray();
        Assert.Equal(2, consumeActivities.Length);
        Assert.Equal("11111111111111111111111111111111", consumeActivities[0].TraceId.ToString());
        Assert.Equal("2222222222222222", consumeActivities[0].ParentSpanId.ToString());
        Assert.Equal(default, consumeActivities[1].ParentSpanId);
        Assert.Null(consumeActivities[1].Parent);
        Assert.NotEqual(default, consumeActivities[1].TraceId);
        AssertStableActivity(consumeActivities[0], "success", expectedDisposition: "consumed");
        AssertStableActivity(consumeActivities[1], "success", expectedDisposition: "consumed");
    }

    [Fact]
    public void RejectedDelivery_EmitsDropWithoutBusinessIdentifiers()
    {
        using var capture = new TelemetryCapture();

        var result = DeliveryValidator().Validate(
            LoadExample("valid"),
            "cp6.crm.secret-topic.v1",
            PartitionKey());

        Assert.False(result.IsValid);
        Assert.Equal(Cp6DaprContractFailure.TopicMismatch, result.Failure);
        var activity = Assert.Single(capture.Activities);
        AssertStableActivity(activity, "rejected", expectedDisposition: "drop");
        Assert.Equal("topic_mismatch", activity.GetTagItem(Cp6TelemetryConventions.ErrorCodeTag));
        Assert.DoesNotContain("secret-topic", RenderActivity(activity), StringComparison.Ordinal);
        var rejected = Assert.Single(
            capture.Measurements,
            measurement => measurement.InstrumentName == "cp6.messaging.rejected");
        AssertStableMetricTags(rejected);
        Assert.DoesNotContain("secret-topic", JsonSerializer.Serialize(rejected.Tags), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPublishContract_ProducesNoTransportOrTelemetrySideEffect()
    {
        using var capture = new TelemetryCapture();
        var transport = new RecordingTransport();

        await Assert.ThrowsAsync<Cp6DaprContractException>(
            () => Publisher(transport).PublishAsync(LoadExample("wrong-type")));

        Assert.Equal(0, transport.PublishAttempts);
        Assert.Empty(capture.Activities);
        Assert.Empty(capture.Measurements);
    }

    [Fact]
    public async Task CallerCancellation_IsPreservedAndUsesStableTelemetry()
    {
        using var capture = new TelemetryCapture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = new RecordingTransport
        {
            PublishException = new OperationCanceledException(cancellation.Token)
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Publisher(transport).PublishAsync(LoadExample("valid"), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        var activity = Assert.Single(capture.Activities);
        AssertStableActivity(activity, "cancelled", expectedDisposition: "cancelled");
        Assert.Equal("cancelled", activity.GetTagItem(Cp6TelemetryConventions.ErrorCodeTag));
    }

    private static void AssertStableActivity(Activity activity, string expectedOutcome, string expectedDisposition)
    {
        Assert.Equal(activity.OperationName, activity.GetTagItem(Cp6TelemetryConventions.OperationTag));
        Assert.Equal(expectedOutcome, activity.GetTagItem(Cp6TelemetryConventions.OutcomeTag));
        Assert.Equal("dapr", activity.GetTagItem(Cp6TelemetryConventions.MessagingTransportTag));
        Assert.Equal(expectedDisposition, activity.GetTagItem(Cp6TelemetryConventions.MessagingDispositionTag));
        var names = activity.TagObjects.Select(tag => tag.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(Cp6TelemetryConventions.AllowedMetricTags.ToHashSet(StringComparer.Ordinal), names);
    }

    private static void AssertStableMetricTags(Measurement measurement)
    {
        var names = measurement.Tags.Select(tag => tag.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(Cp6TelemetryConventions.AllowedMetricTags.ToHashSet(StringComparer.Ordinal), names);
        var rendered = JsonSerializer.Serialize(measurement.Tags);
        foreach (var forbidden in new[] { "tenant", "aggregate", "correlation", "secret", "host", "exception" })
        {
            Assert.DoesNotContain(forbidden, rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string RenderActivity(Activity activity) => JsonSerializer.Serialize(
        activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value));

    private static Cp6DaprEventPublisher Publisher(ICp6DaprTransport transport) =>
        new(transport, new Cp6CloudEventValidator(Cp6ContractBundle.Load(ContractRoot)));

    private static Cp6DaprDeliveryValidator DeliveryValidator() =>
        new(new Cp6CloudEventValidator(Cp6ContractBundle.Load(ContractRoot)));

    private static ReadOnlyMemory<byte> AddTelemetry(
        ReadOnlyMemory<byte> source,
        params (string Name, string Value)[] properties)
    {
        var json = Encoding.UTF8.GetString(source.Span);
        var insertion = string.Join(
            string.Empty,
            properties.Select(property => $"\"{property.Name}\":{JsonSerializer.Serialize(property.Value)},"));
        return Encoding.UTF8.GetBytes("{" + insertion + json.TrimStart()[1..]);
    }

    private static string Topic() => "cp6.platform.contract-example-changed.v1";

    private static string PartitionKey() => "11111111-1111-4111-8111-111111111111/example-1";

    private static ReadOnlyMemory<byte> LoadExample(string name)
    {
        var bundle = Cp6ContractBundle.Load(ContractRoot);
        var example = Assert.Single(Assert.Single(bundle.Entries).Examples, candidate => candidate.Name == name);
        return File.ReadAllBytes(bundle.GetAssetPath(example.Path));
    }

    private static string FindContractRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "contracts");
            if (File.Exists(Path.Combine(candidate, "contract-bundle.v1.json")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CP6 event contract bundle.");
    }

    private sealed class TelemetryCapture : IDisposable
    {
        private readonly ActivityListener activityListener;
        private readonly MeterListener meterListener;

        public TelemetryCapture()
        {
            activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Cp6TelemetrySources.Messaging,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Activities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == Cp6TelemetryMeters.Messaging)
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

    private sealed class RecordingTransport : ICp6DaprTransport
    {
        public Exception? PublishException { get; init; }

        public int PublishAttempts { get; private set; }

        public Task PublishAsync(
            string pubsubName,
            string topicName,
            ReadOnlyMemory<byte> body,
            string contentType,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default)
        {
            PublishAttempts++;
            return PublishException is null
                ? Task.CompletedTask
                : Task.FromException(PublishException);
        }

        public Task<HttpResponseMessage> InvokeAsync(
            HttpMethod method,
            string appId,
            string methodName,
            HttpContent? content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

[CollectionDefinition(nameof(MessagingTelemetryCollection), DisableParallelization = true)]
public sealed class MessagingTelemetryCollection;
