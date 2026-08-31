using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using CP6.Platform.Deployment;
using CP6.Platform.Messaging;
using Dapr.Client;

const int UnknownRoleExitCode = 64;
const int MaximumEventBytes = 1_048_576;
const string PublisherRole = "publisher";
const string ReceiverRole = "receiver";
const string ProbeRole = "probe";
const string UnauthorizedRole = "unauthorized";
const string ProbeEventType = "com.gtx537.platform.contract-example.changed.v1";
const string ProbeRegionLabel = "TEST";
const string ProbeRegion = "test";
const string DaprHttpEndpointConfigurationKey = "DAPR_HTTP_ENDPOINT";
const string DaprGrpcEndpointConfigurationKey = "DAPR_GRPC_ENDPOINT";
const string DirectKafkaHost = "kafka";
const int DirectKafkaPort = 9092;

if (args.Length != 1 ||
    args[0] is not (PublisherRole or ReceiverRole or ProbeRole or UnauthorizedRole))
{
    Environment.ExitCode = UnknownRoleExitCode;
    return;
}

var role = args[0];
var profilePath = Path.Combine(
    AppContext.BaseDirectory,
    "contracts",
    "p09",
    "non-production-runtime-profile.valid.json");
var contractRoot = Path.Combine(AppContext.BaseDirectory, "contracts");
var profile = Cp6P09RuntimeProfile.Parse(await File.ReadAllBytesAsync(profilePath));
if (!string.Equals(profile.EventType, ProbeEventType, StringComparison.Ordinal))
{
    throw new InvalidDataException("The P09 Profile does not reference the canonical P04 probe contract.");
}

var bundle = Cp6ContractBundle.Load(contractRoot);
var cloudEventValidator = new Cp6CloudEventValidator(bundle);
var builder = WebApplication.CreateBuilder(Array.Empty<string>());
Uri? daprHttpEndpointUri = null;
Uri? daprGrpcEndpointUri = null;
if (role is PublisherRole or UnauthorizedRole &&
    (!Cp6P09DaprEndpointValidator.TryParse(
        builder.Configuration[DaprHttpEndpointConfigurationKey] ??
            Cp6P09DaprEndpointValidator.DefaultHttpEndpoint,
        role,
        Cp6P09DaprEndpointKind.Http,
        out daprHttpEndpointUri) ||
     !Cp6P09DaprEndpointValidator.TryParse(
         builder.Configuration[DaprGrpcEndpointConfigurationKey] ??
            Cp6P09DaprEndpointValidator.DefaultGrpcEndpoint,
         role,
         Cp6P09DaprEndpointKind.Grpc,
         out daprGrpcEndpointUri)))
{
    throw new InvalidDataException("The local Dapr sidecar endpoints are invalid.");
}

DaprClient? daprClient = role is PublisherRole or UnauthorizedRole
    ? new DaprClientBuilder()
        .UseHttpEndpoint(daprHttpEndpointUri!.AbsoluteUri)
        .UseGrpcEndpoint(daprGrpcEndpointUri!.AbsoluteUri)
        .Build()
    : null;
HttpClient? invocationClient = role == PublisherRole
    ? new HttpClient { BaseAddress = daprHttpEndpointUri }
    : null;
Cp6DaprServiceInvoker? serviceInvoker = role == PublisherRole
    ? new Cp6DaprServiceInvoker(new Cp6DaprTransport(daprClient!, invocationClient!))
    : null;
var received = new ReceivedEventStore();
var app = builder.Build();
app.Lifetime.ApplicationStopped.Register(() =>
{
    invocationClient?.Dispose();
    daprClient?.Dispose();
});

if (role == PublisherRole)
{
    app.MapGet("/healthz", () => Results.Json(new
    {
        role,
        profileId = profile.ProfileId,
        profileSha256 = profile.Sha256
    }));

    app.MapPost("/invoke-positive", async (CancellationToken cancellationToken) =>
    {
        var invokerTrace = RequireCurrentTrace();
        var correlationId = $"p09-{Guid.NewGuid():N}";
        using var content = JsonContent.Create(new InvocationProbeRequest(correlationId));
        using var invokeResponse = await serviceInvoker!.InvokeAsync(
            HttpMethod.Post,
            profile.ReceiverAppId,
            "invoked",
            content,
            cancellationToken);
        var response = await invokeResponse.Content.ReadFromJsonAsync<InvokedTraceObservation>(
            cancellationToken: cancellationToken);
        if (response is null ||
            !string.Equals(response.AppId, profile.ReceiverAppId, StringComparison.Ordinal) ||
            !string.Equals(response.CorrelationId, correlationId, StringComparison.Ordinal) ||
            !Cp6P09TraceTopology.TryParseObservedContext(
                response.InvocationTraceId,
                response.InvokedSpanId,
                isRemote: true,
                out var invokedTrace) ||
            !Cp6P09TraceTopology.TryParseObservedSpanId(
                response.InvokedParentSpanId,
                out var invokedParentSpanId) ||
            !Cp6P09TraceTopology.TryCreateInvocation(
                invokerTrace,
                invokedTrace,
                invokedParentSpanId,
                out var invocationTrace))
        {
            return Results.Json(
                new { code = "invoke-response-invalid" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Json(new InvocationProbeEvidence(
            response.AppId,
            response.CorrelationId,
            invocationTrace.InvocationTraceId,
            invocationTrace.InvokerSpanId,
            invocationTrace.InvokedSpanId,
            invocationTrace.InvokedParentSpanId));
    });

    app.MapPost("/publish-positive", async (
        PublishProbeRequest request,
        CancellationToken cancellationToken) =>
    {
        if (!IsIdentifier(request.EventId) || !IsIdentifier(request.PartitionKey))
        {
            return Results.BadRequest(new { code = "publish-input-invalid" });
        }

        var publisherTrace = RequireCurrentTrace();
        var structuredEvent = CreateStructuredProbeEvent(
            profile,
            request.EventId,
            request.PartitionKey,
            publisherTrace);
        var validation = cloudEventValidator.Validate(structuredEvent);
        if (!validation.IsValid)
        {
            return Results.Json(
                new { code = "publish-contract-invalid" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        await daprClient!.PublishByteEventAsync(
            profile.PublishComponentName,
            profile.TopicName,
            structuredEvent,
            Cp6CloudEventCodec.StructuredContentType,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Cp6DaprKafkaConventions.PartitionKeyMetadata] = request.PartitionKey
            },
            cancellationToken);

        return Results.Json(new PublishProbeReceipt(
            request.EventId,
            request.PartitionKey,
            ProbeRegionLabel,
            profile.TopicName,
            profile.PublishComponentName));
    });
}
else if (role == ReceiverRole)
{
    app.MapGet("/dapr/subscribe", () => Results.Json(new[]
    {
        new
        {
            pubsubname = profile.SubscribeComponentName,
            topic = profile.TopicName,
            route = "/events/deployment-probe"
        }
    }));

    app.MapPost("/events/deployment-probe", async (
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var body = await ReadBoundedBodyAsync(context.Request, MaximumEventBytes, cancellationToken);
        if (body is null)
        {
            return Results.Json(new { status = "DROP", code = "event-size-invalid" });
        }

        var topic = context.Request.Headers["__topic"].ToString();
        var partitionKey = context.Request.Headers["__key"].ToString();
        var validation = cloudEventValidator.Validate(body);
        if (!validation.IsValid || validation.CloudEvent is null)
        {
            return Results.Json(new { status = "DROP", code = "event-contract-invalid" });
        }

        var cloudEvent = validation.CloudEvent;
        var region = (string?)cloudEvent[Cp6CloudEventAttributes.Region];
        var aggregateId = (string?)cloudEvent[Cp6CloudEventAttributes.AggregateId];
        var traceParent = (string?)cloudEvent[Cp6CloudEventAttributes.TraceParent];
        var receiverTrace = RequireCurrentTrace();
        var receiverParentSpanId = RequireCurrentParentSpanId();
        if (!string.Equals(cloudEvent.Type, profile.EventType, StringComparison.Ordinal) ||
            !string.Equals(region, ProbeRegion, StringComparison.Ordinal) ||
            !string.Equals(topic, profile.TopicName, StringComparison.Ordinal) ||
            !IsIdentifier(partitionKey) ||
            !string.Equals(partitionKey, aggregateId, StringComparison.Ordinal) ||
            !TryParseTrace(traceParent, out var publisherTrace) ||
            !Cp6P09TraceTopology.TryCreateDelivery(
                publisherTrace,
                receiverTrace,
                receiverParentSpanId,
                out var deliveryTrace))
        {
            return Results.Json(new { status = "DROP", code = "delivery-contract-invalid" });
        }

        received.Set(new ReceivedEventEvidence(
            cloudEvent.Id!,
            cloudEvent.Type!,
            topic,
            partitionKey,
            ProbeRegionLabel,
            deliveryTrace.TraceId,
            deliveryTrace.PublisherSpanId,
            deliveryTrace.ReceiverSpanId,
            deliveryTrace.ReceiverParentSpanId,
            true));
        return Results.Json(new { status = "SUCCESS" });
    });

    app.MapPost("/invoked", (InvocationProbeRequest request) =>
    {
        if (!IsIdentifier(request.CorrelationId))
        {
            return Results.BadRequest(new { code = "correlation-invalid" });
        }

        var trace = RequireCurrentTrace();
        return Results.Json(new InvokedTraceObservation(
            profile.ReceiverAppId,
            request.CorrelationId,
            trace.TraceId.ToHexString(),
            trace.SpanId.ToHexString(),
            RequireCurrentParentSpanId().ToHexString()));
    });

    app.MapGet("/received/{eventId}", (string eventId) =>
    {
        if (!IsIdentifier(eventId))
        {
            return Results.NotFound();
        }

        var value = received.Get(eventId);
        return value is null ? Results.NotFound() : Results.Json(value);
    });
}
else if (role == ProbeRole)
{
    app.MapGet("/direct-kafka", async (HttpContext context) =>
    {
        IPAddress[] addresses;
        try
        {
            using var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            dnsTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            addresses = (await Dns.GetHostAddressesAsync(DirectKafkaHost, dnsTimeout.Token))
                .Distinct()
                .ToArray();
        }
        catch (SocketException)
        {
            return Results.Json(new { denied = true, code = "direct-kafka-denied" });
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            return Results.Json(new { denied = true, code = "direct-kafka-denied" });
        }

        var reachable = await Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            addresses,
            async (address, cancellationToken) =>
            {
                using var client = new TcpClient(address.AddressFamily);
                await client.ConnectAsync(address, DirectKafkaPort, cancellationToken);
                if (!client.Connected)
                {
                    throw new SocketException((int)SocketError.NotConnected);
                }
            },
            context.RequestAborted,
            TimeSpan.FromSeconds(1));
        if (reachable)
        {
            return Results.Json(
                new { denied = false, code = "direct-kafka-reachable" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Json(new { denied = true, code = "direct-kafka-denied" });
    });
}
else
{
    app.MapPost("/publish", async (CancellationToken cancellationToken) =>
    {
        var eventId = $"unauthorized-{Guid.NewGuid():N}";
        var partitionKey = $"probe-{Guid.NewGuid():N}";
        var structuredEvent = CreateStructuredProbeEvent(
            profile,
            eventId,
            partitionKey,
            RequireCurrentTrace());
        try
        {
            await daprClient!.PublishByteEventAsync(
                profile.PublishComponentName,
                profile.TopicName,
                structuredEvent,
                Cp6CloudEventCodec.StructuredContentType,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Cp6DaprKafkaConventions.PartitionKeyMetadata] = partitionKey
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            var outcome = Cp6P09UnauthorizedPublishClassifier.Classify(
                exception,
                cancellationToken.IsCancellationRequested);
            if (outcome == Cp6P09UnauthorizedPublishOutcome.CallerCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            if (outcome == Cp6P09UnauthorizedPublishOutcome.AllowedDenied)
            {
                return Results.Json(new { denied = true, code = "appid-scope-denied" });
            }

            return Results.Json(
                new { denied = false, code = "dapr-publish-infrastructure-failure" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(
            new { denied = false, code = "appid-scope-bypass" },
            statusCode: StatusCodes.Status500InternalServerError);
    });
}

await app.RunAsync();

static byte[] CreateStructuredProbeEvent(
    Cp6P09RuntimeProfile profile,
    string eventId,
    string partitionKey,
    ActivityContext traceContext)
{
    var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111", CultureInfo.InvariantCulture);
    using var data = JsonDocument.Parse(
        """
        {"resourceId":"22222222-2222-4222-8222-222222222222","version":1}
        """);
    var descriptor = new Cp6CloudEventDescriptor(
        eventId,
        new Uri("urn:cp6:platform", UriKind.Absolute),
        profile.EventType,
        $"tenants/{tenantId:D}/contract-examples/{partitionKey}",
        DateTimeOffset.UtcNow,
        new Uri("https://contracts.cp6.uk/events/platform/contract-example-changed/v1/schema.json", UriKind.Absolute),
        tenantId,
        eventId,
        "p09-probe",
        partitionKey,
        1,
        "1.0.0",
        ProbeRegion);
    var cloudEvent = Cp6CloudEventCodec.Create(descriptor, data.RootElement, traceContext);
    return Cp6CloudEventCodec.EncodeStructured(cloudEvent).ToArray();
}

static ActivityContext RequireCurrentTrace()
{
    var context = Activity.Current?.Context;
    if (context is not { TraceId: var traceId, SpanId: var spanId } ||
        traceId == default ||
        spanId == default)
    {
        throw new InvalidOperationException("A bounded W3C trace context is required for the P09 probe.");
    }

    return context.Value;
}

static bool TryParseTrace(string? traceParent, out ActivityContext context)
{
    context = default;
    return !string.IsNullOrEmpty(traceParent) &&
        traceParent.Length <= 55 &&
        ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out context) &&
        context.TraceId != default &&
        context.SpanId != default;
}

static ActivitySpanId RequireCurrentParentSpanId()
{
    var parentSpanId = Activity.Current?.ParentSpanId ?? default;
    if (parentSpanId == default)
    {
        throw new InvalidOperationException("An observed W3C parent Span ID is required for the P09 probe.");
    }

    return parentSpanId;
}

static bool IsIdentifier([NotNullWhen(true)] string? value)
{
    if (value is not { Length: >= 1 and <= 128 } || !char.IsAsciiLetterOrDigit(value[0]))
    {
        return false;
    }

    return value.AsSpan(1).IndexOfAnyExcept(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._:-") < 0;
}

static async Task<byte[]?> ReadBoundedBodyAsync(
    HttpRequest request,
    int maximumBytes,
    CancellationToken cancellationToken)
{
    if (request.ContentLength is < 0 || request.ContentLength > maximumBytes)
    {
        return null;
    }

    using var output = new MemoryStream(Math.Min(maximumBytes, (int)(request.ContentLength ?? 0)));
    var buffer = new byte[16 * 1024];
    while (true)
    {
        var read = await request.Body.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            return output.ToArray();
        }

        if (output.Length + read > maximumBytes)
        {
            return null;
        }

        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
    }
}

internal sealed class ReceivedEventStore
{
    private ReceivedEventEvidence? value;

    public ReceivedEventEvidence? Get(string eventId)
    {
        var current = Volatile.Read(ref value);
        return current is not null && string.Equals(current.EventId, eventId, StringComparison.Ordinal)
            ? current
            : null;
    }

    public void Set(ReceivedEventEvidence evidence) => Volatile.Write(ref value, evidence);
}

internal sealed record PublishProbeRequest(string? EventId, string? PartitionKey);

internal sealed record PublishProbeReceipt(
    string EventId,
    string PartitionKey,
    string Region,
    string Topic,
    string Component);

internal sealed record InvocationProbeRequest(string? CorrelationId);

internal sealed record InvokedTraceObservation(
    string AppId,
    string CorrelationId,
    string InvocationTraceId,
    string InvokedSpanId,
    string InvokedParentSpanId);

internal sealed record InvocationProbeEvidence(
    string AppId,
    string CorrelationId,
    string InvocationTraceId,
    string InvokerSpanId,
    string InvokedSpanId,
    string InvokedParentSpanId);

internal sealed record ReceivedEventEvidence(
    string EventId,
    string EventType,
    string TopicName,
    string PartitionKey,
    string Region,
    string TraceId,
    string PublisherSpanId,
    string ReceiverSpanId,
    string ReceiverParentSpanId,
    bool ContractValid);
