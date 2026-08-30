using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.Messaging;

namespace CP6.Platform.UnitTests;

public sealed class TraceContextContractTests
{
    private const string TraceParent = "00-11111111111111111111111111111111-2222222222222222-01";
    private const string TraceState = "vendor=value";
    private static readonly string ContractRoot = FindContractRoot();

    [Fact]
    public void Create_InjectsValidW3cContextWithoutBaggage()
    {
        var context = new ActivityContext(
            ActivityTraceId.CreateFromString("11111111111111111111111111111111"),
            ActivitySpanId.CreateFromString("2222222222222222"),
            ActivityTraceFlags.Recorded,
            TraceState);
        using var baggageActivity = new Activity("baggage-source");
        baggageActivity.Start();
        baggageActivity.AddBaggage("must-not-appear", "secret-value");

        var cloudEvent = Cp6CloudEventCodec.Create(Descriptor(), Data(), context);
        var encoded = Encoding.UTF8.GetString(Cp6CloudEventCodec.EncodeStructured(cloudEvent).Span);

        Assert.Equal(TraceParent, cloudEvent[Cp6CloudEventAttributes.TraceParent]);
        Assert.Equal(TraceState, cloudEvent[Cp6CloudEventAttributes.TraceState]);
        Assert.DoesNotContain("baggage", encoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-appear", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithNoContext_LeavesBothTelemetryFieldsAbsent()
    {
        var cloudEvent = Cp6CloudEventCodec.Create(Descriptor(), Data(), activityContext: null);

        Assert.Null(cloudEvent[Cp6CloudEventAttributes.TraceParent]);
        Assert.Null(cloudEvent[Cp6CloudEventAttributes.TraceState]);
    }

    [Fact]
    public void Create_EmitsTraceParentWithoutOptionalTraceState()
    {
        var context = new ActivityContext(
            ActivityTraceId.CreateFromString("11111111111111111111111111111111"),
            ActivitySpanId.CreateFromString("2222222222222222"),
            ActivityTraceFlags.None);

        var cloudEvent = Cp6CloudEventCodec.Create(Descriptor(), Data(), context);

        Assert.Equal("00-11111111111111111111111111111111-2222222222222222-00", cloudEvent[Cp6CloudEventAttributes.TraceParent]);
        Assert.Null(cloudEvent[Cp6CloudEventAttributes.TraceState]);
    }

    [Fact]
    public void ExistingCreateOverload_CapturesCurrentActivityContext()
    {
        using var activity = new Activity("create-event");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var cloudEvent = Cp6CloudEventCodec.Create(Descriptor(), Data());

        Assert.NotNull(cloudEvent[Cp6CloudEventAttributes.TraceParent]);
        Assert.StartsWith("00-" + activity.TraceId + "-", (string)cloudEvent[Cp6CloudEventAttributes.TraceParent]!);
    }

    [Fact]
    public void Delivery_ExtractsRemoteParentAfterBusinessValidation()
    {
        var body = AddTelemetry(LoadExample("valid"), ("traceparent", TraceParent), ("tracestate", TraceState));

        var result = Validator().Validate(body, Topic(), PartitionKey());

        Assert.True(result.IsValid);
        Assert.NotNull(result.ParentContext);
        Assert.True(result.ParentContext.Value.IsRemote);
        Assert.Equal("11111111111111111111111111111111", result.ParentContext.Value.TraceId.ToString());
        Assert.Equal("2222222222222222", result.ParentContext.Value.SpanId.ToString());
        Assert.Equal(ActivityTraceFlags.Recorded, result.ParentContext.Value.TraceFlags);
        Assert.Equal(TraceState, result.ParentContext.Value.TraceState);
    }

    [Theory]
    [MemberData(nameof(InvalidTelemetry))]
    public void InvalidTelemetry_DoesNotInvalidateBusinessMessage(
        ReadOnlyMemory<byte> body,
        string rejectedValue)
    {
        var result = Validator().Validate(body, Topic(), PartitionKey());

        Assert.True(result.IsValid);
        Assert.NotNull(result.CloudEvent);
        Assert.Null(result.ParentContext);
        Assert.DoesNotContain(rejectedValue, result.Failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidTraceOnInvalidBusinessMessage_IsNeverAttached()
    {
        var body = AddTelemetry(LoadExample("wrong-type"), ("traceparent", TraceParent));

        var result = Validator().Validate(body, Topic(), PartitionKey());

        Assert.False(result.IsValid);
        Assert.Null(result.CloudEvent);
        Assert.Null(result.ParentContext);
    }

    [Fact]
    public void RejectedTraceContextMetric_UsesOnlyStableErrorCode()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == Cp6TelemetryMeters.Messaging &&
                    instrument.Name == "cp6.messaging.trace_context.rejected")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray())));
        listener.Start();
        var rejected = "secret-invalid-trace-value";
        var body = AddTelemetry(LoadExample("valid"), ("traceparent", rejected));

        var result = Validator().Validate(body, Topic(), PartitionKey());

        Assert.True(result.IsValid);
        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        var tag = Assert.Single(measurement.Tags);
        Assert.Equal(Cp6TelemetryConventions.ErrorCodeTag, tag.Key);
        Assert.Equal("invalid_trace_context", tag.Value);
        Assert.DoesNotContain(rejected, JsonSerializer.Serialize(measurement), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> InvalidTelemetry()
    {
        var valid = LoadExample("valid");
        var malformed = "00-11111111111111111111111111111111-zero-span-01";
        yield return [AddTelemetry(valid, ("traceparent", malformed)), malformed];

        var overlongParent = new string('a', 56);
        yield return [AddTelemetry(valid, ("traceparent", overlongParent)), overlongParent];

        var overlongState = new string('s', 513);
        yield return [AddTelemetry(valid, ("traceparent", TraceParent), ("tracestate", overlongState)), overlongState];

        const string malformedState = "vendor==bad";
        yield return [AddTelemetry(valid, ("traceparent", TraceParent), ("tracestate", malformedState)), malformedState];

        var duplicate = AddTelemetry(valid, ("traceparent", TraceParent), ("traceparent", malformed));
        yield return [duplicate, malformed];

        var stateWithoutParent = AddTelemetry(valid, ("tracestate", "orphan=value"));
        yield return [stateWithoutParent, "orphan=value"];
    }

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

    private static Cp6DaprDeliveryValidator Validator() =>
        new(new Cp6CloudEventValidator(Cp6ContractBundle.Load(ContractRoot)));

    private static string Topic() => "cp6.platform.contract-example-changed.v1";

    private static string PartitionKey() => "11111111-1111-4111-8111-111111111111/example-1";

    private static Cp6CloudEventDescriptor Descriptor()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var identity = Cp6EventContractIdentity.Parse("com.gtx537.platform.contract-example.changed.v1");
        return new Cp6CloudEventDescriptor(
            "evt-trace-1",
            new Uri("urn:cp6:platform"),
            identity.EventType,
            $"tenants/{tenantId:D}/contract-examples/example-trace",
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            identity.SchemaId,
            tenantId,
            "corr-trace-1",
            "cmd-trace-1",
            "example-trace",
            1,
            "1.0.0",
            "na");
    }

    private static JsonElement Data()
    {
        using var document = JsonDocument.Parse("""
            {
              "resourceId": "22222222-2222-4222-8222-222222222222",
              "version": 1
            }
            """);
        return document.RootElement.Clone();
    }

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
}
