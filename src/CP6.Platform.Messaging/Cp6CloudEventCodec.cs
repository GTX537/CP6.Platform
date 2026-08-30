using System.Diagnostics;
using System.Net.Mime;
using System.Text.Json;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;

namespace CP6.Platform.Messaging;

/// <summary>
/// Creates and serializes the single CP6 CloudEvents 1.0 structured JSON envelope.
/// </summary>
public static class Cp6CloudEventCodec
{
    public const string StructuredContentType = "application/cloudevents+json";
    public const string JsonDataContentType = "application/json";

    private static readonly JsonEventFormatter Formatter = new();

    public static CloudEvent Create(Cp6CloudEventDescriptor descriptor, JsonElement data)
        => Create(descriptor, data, Activity.Current?.Context);

    public static CloudEvent Create(
        Cp6CloudEventDescriptor descriptor,
        JsonElement data,
        ActivityContext? activityContext)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("CP6 event data must be a JSON object.", nameof(data));
        }

        var identity = ValidateDescriptor(descriptor);
        var cloudEvent = new CloudEvent(Cp6CloudEventAttributes.All)
        {
            Id = descriptor.Id,
            Source = descriptor.Source,
            Type = descriptor.Type,
            Subject = descriptor.Subject,
            Time = descriptor.Time,
            DataContentType = JsonDataContentType,
            DataSchema = descriptor.DataSchema,
            Data = data.Clone()
        };

        cloudEvent[Cp6CloudEventAttributes.TenantId] = descriptor.TenantId.ToString("D");
        cloudEvent[Cp6CloudEventAttributes.CorrelationId] = descriptor.CorrelationId;
        cloudEvent[Cp6CloudEventAttributes.CausationId] = descriptor.CausationId;
        cloudEvent[Cp6CloudEventAttributes.AggregateId] = descriptor.AggregateId;
        cloudEvent[Cp6CloudEventAttributes.AggregateVersion] = descriptor.AggregateVersion;
        cloudEvent[Cp6CloudEventAttributes.SchemaVersion] = descriptor.SchemaVersion;
        cloudEvent[Cp6CloudEventAttributes.Region] = descriptor.Region;
        InjectTraceContext(cloudEvent, activityContext);

        ValidateEnvelope(cloudEvent, identity);
        return cloudEvent;
    }

    public static ReadOnlyMemory<byte> EncodeStructured(CloudEvent cloudEvent)
    {
        ArgumentNullException.ThrowIfNull(cloudEvent);
        ValidateEnvelope(cloudEvent);
        var encoded = Formatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
        if (!string.Equals(contentType.MediaType, StructuredContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CloudEvents formatter returned an unexpected structured media type.");
        }

        return encoded;
    }

    public static CloudEvent DecodeStructured(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            throw new ArgumentException("Structured CloudEvent body must not be empty.", nameof(body));
        }

        var cloudEvent = Formatter.DecodeStructuredModeMessage(
            body,
            new ContentType(StructuredContentType),
            Cp6CloudEventAttributes.All);
        ValidateEnvelope(cloudEvent);
        return cloudEvent;
    }

    public static void ValidateEnvelope(CloudEvent cloudEvent)
    {
        ArgumentNullException.ThrowIfNull(cloudEvent);
        var identity = Cp6EventContractIdentity.Parse(cloudEvent.Type ?? string.Empty);
        ValidateEnvelope(cloudEvent, identity);
    }

    private static Cp6EventContractIdentity ValidateDescriptor(Cp6CloudEventDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);
        if (descriptor.Id.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Event id cannot exceed 128 characters.");
        }
        Cp6CloudEventAttributes.CausationId.Validate(descriptor.Id);

        var identity = Cp6EventContractIdentity.Parse(descriptor.Type);
        if (descriptor.DataSchema != identity.SchemaId)
        {
            throw new ArgumentException("dataschema must be the canonical schema identifier for the event type.", nameof(descriptor));
        }

        if (!descriptor.Source.IsAbsoluteUri || descriptor.Source.AbsoluteUri != $"urn:cp6:{identity.Producer}")
        {
            throw new ArgumentException("source must be the canonical urn:cp6:<producer> value for the event type.", nameof(descriptor));
        }

        var tenantSubjectPrefix = $"tenants/{descriptor.TenantId:D}/";
        if (!descriptor.Subject.StartsWith(tenantSubjectPrefix, StringComparison.Ordinal) || descriptor.Subject.Length > 512)
        {
            throw new ArgumentException("subject must be tenant-scoped and cannot exceed 512 characters.", nameof(descriptor));
        }

        if (descriptor.Time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("time must use UTC offset Z.", nameof(descriptor));
        }

        return identity;
    }

    private static void ValidateEnvelope(CloudEvent cloudEvent, Cp6EventContractIdentity identity)
    {
        cloudEvent.Validate();
        if (cloudEvent.SpecVersion != CloudEventsSpecVersion.V1_0)
        {
            throw new ArgumentException("Only CloudEvents 1.0 is accepted.", nameof(cloudEvent));
        }

        if (!string.Equals(cloudEvent.DataContentType, JsonDataContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("datacontenttype must be application/json.", nameof(cloudEvent));
        }

        if (cloudEvent.DataSchema != identity.SchemaId)
        {
            throw new ArgumentException("dataschema does not match the canonical event type schema.", nameof(cloudEvent));
        }

        if (cloudEvent.Source?.AbsoluteUri != $"urn:cp6:{identity.Producer}")
        {
            throw new ArgumentException("source does not match the event type producer.", nameof(cloudEvent));
        }

        if (cloudEvent.Time is null || cloudEvent.Time.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("time is required and must use UTC offset Z.", nameof(cloudEvent));
        }

        if (cloudEvent.Data is not JsonElement { ValueKind: JsonValueKind.Object })
        {
            throw new ArgumentException("data must be a JSON object.", nameof(cloudEvent));
        }

        var tenantId = (string?)cloudEvent[Cp6CloudEventAttributes.TenantId];
        if (tenantId is null || cloudEvent.Subject is null ||
            !cloudEvent.Subject.StartsWith($"tenants/{tenantId}/", StringComparison.Ordinal))
        {
            throw new ArgumentException("subject must be scoped to the event tenantid.", nameof(cloudEvent));
        }

        foreach (var attribute in Cp6CloudEventAttributes.Required)
        {
            if (cloudEvent[attribute] is null)
            {
                throw new ArgumentException($"Required CP6 extension attribute '{attribute.Name}' is missing.", nameof(cloudEvent));
            }
        }
    }

    private static void InjectTraceContext(CloudEvent cloudEvent, ActivityContext? activityContext)
    {
        if (activityContext is not { } context ||
            context.TraceId == default ||
            context.SpanId == default)
        {
            return;
        }

        var flags = (context.TraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        var traceParent = $"00-{context.TraceId}-{context.SpanId}-{flags}";
        var traceState = string.IsNullOrEmpty(context.TraceState) ? null : context.TraceState;
        if (traceParent.Length > 55 ||
            traceState?.Length > 512 ||
            (traceState is not null && !Cp6TraceContextCodec.IsValidTraceState(traceState)) ||
            !ActivityContext.TryParse(traceParent, traceState, isRemote: false, out _))
        {
            throw new ArgumentException("Activity context is not a valid bounded W3C trace context.", nameof(activityContext));
        }

        cloudEvent[Cp6CloudEventAttributes.TraceParent] = traceParent;
        if (traceState is not null)
        {
            cloudEvent[Cp6CloudEventAttributes.TraceState] = traceState;
        }
    }
}
