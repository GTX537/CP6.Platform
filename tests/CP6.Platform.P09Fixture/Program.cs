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
const string DaprEndpointConfigurationKey = "DAPR_HTTP_ENDPOINT";
const string DefaultDaprEndpoint = "http://127.0.0.1:3500";
const int DaprSidecarPort = 3500;
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
var daprEndpoint = builder.Configuration[DaprEndpointConfigurationKey] ?? DefaultDaprEndpoint;
if (!Uri.TryCreate(daprEndpoint, UriKind.Absolute, out var daprEndpointUri) ||
    !string.Equals(daprEndpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
    !string.IsNullOrEmpty(daprEndpointUri.UserInfo) ||
    daprEndpointUri.Port != DaprSidecarPort ||
    daprEndpointUri.AbsolutePath != "/" ||
    !string.IsNullOrEmpty(daprEndpointUri.Query) ||
    !string.IsNullOrEmpty(daprEndpointUri.Fragment) ||
    !IsAllowedDaprHost(daprEndpointUri.IdnHost))
{
    throw new InvalidDataException("The local Dapr endpoint is invalid.");
}

DaprClient? daprClient = role is PublisherRole or UnauthorizedRole
    ? new DaprClientBuilder().UseHttpEndpoint(daprEndpointUri.AbsoluteUri).Build()
    : null;
HttpClient? invocationClient = role == PublisherRole
    ? new HttpClient { BaseAddress = daprEndpointUri }
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
        var correlationId = $"p09-{Guid.NewGuid():N}";
        using var content = JsonContent.Create(new InvocationProbeRequest(correlationId));
        using var invokeResponse = await serviceInvoker!.InvokeAsync(
            HttpMethod.Post,
            profile.ReceiverAppId,
            "invoked",
            content,
            cancellationToken);
        var response = await invokeResponse.Content.ReadFromJsonAsync<InvocationProbeEvidence>(
            cancellationToken: cancellationToken);
        if (response is null ||
            !string.Equals(response.AppId, profile.ReceiverAppId, StringComparison.Ordinal) ||
            !string.Equals(response.CorrelationId, correlationId, StringComparison.Ordinal) ||
            !IsTraceId(response.TraceId) ||
            !IsSpanId(response.SpanId))
        {
            return Results.Json(
                new { code = "invoke-response-invalid" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Json(response);
    });

    app.MapPost("/publish-positive", async (
        PublishProbeRequest request,
        CancellationToken cancellationToken) =>
    {
        if (!IsIdentifier(request.EventId) || !IsIdentifier(request.PartitionKey))
        {
            return Results.BadRequest(new { code = "publish-input-invalid" });
        }

        var structuredEvent = CreateStructuredProbeEvent(
            profile,
            request.EventId,
            request.PartitionKey,
            RequireCurrentTrace());
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

        return Results.Json(new
        {
            eventId = request.EventId,
            partitionKey = request.PartitionKey,
            region = ProbeRegionLabel,
            topic = profile.TopicName,
            component = profile.PublishComponentName
        });
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
        if (!string.Equals(cloudEvent.Type, profile.EventType, StringComparison.Ordinal) ||
            !string.Equals(region, ProbeRegion, StringComparison.Ordinal) ||
            !string.Equals(topic, profile.TopicName, StringComparison.Ordinal) ||
            !IsIdentifier(partitionKey) ||
            !string.Equals(partitionKey, aggregateId, StringComparison.Ordinal) ||
            !TryParseTrace(traceParent, out var traceContext))
        {
            return Results.Json(new { status = "DROP", code = "delivery-contract-invalid" });
        }

        received.Set(new ReceivedEventEvidence(
            cloudEvent.Id!,
            cloudEvent.Type!,
            topic,
            partitionKey,
            ProbeRegionLabel,
            traceContext.TraceId.ToHexString(),
            traceContext.SpanId.ToHexString(),
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
        return Results.Json(new InvocationProbeEvidence(
            profile.ReceiverAppId,
            request.CorrelationId,
            trace.TraceId.ToHexString(),
            trace.SpanId.ToHexString()));
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Results.Json(new { denied = true, code = "appid-scope-denied" });
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

static bool IsTraceId(string? value) =>
    value is { Length: 32 } && value.All(IsLowerHex) && value.Any(character => character != '0');

static bool IsSpanId(string? value) =>
    value is { Length: 16 } && value.All(IsLowerHex) && value.Any(character => character != '0');

static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';

static bool IsAllowedDaprHost(string host)
{
    if (IPAddress.TryParse(host, out var address))
    {
        return IPAddress.IsLoopback(address);
    }

    return host is "localhost" or "publisher-dapr" or "receiver-dapr" or "unauthorized-dapr";
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

internal sealed record InvocationProbeRequest(string? CorrelationId);

internal sealed record InvocationProbeEvidence(
    string AppId,
    string CorrelationId,
    string TraceId,
    string SpanId);

internal sealed record ReceivedEventEvidence(
    string EventId,
    string EventType,
    string TopicName,
    string PartitionKey,
    string Region,
    string TraceId,
    string ParentSpanId,
    bool ContractValid);
