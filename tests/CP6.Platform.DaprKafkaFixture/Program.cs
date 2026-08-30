using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Platform.Messaging;
using Dapr.Client;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var activityListener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "CP6.Platform.Messaging",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
};
ActivitySource.AddActivityListener(activityListener);

var role = Environment.GetEnvironmentVariable("CP6_P05_ROLE") ?? "receiver";
var contractRoot = Environment.GetEnvironmentVariable("CP6_CONTRACT_ROOT") ?? "/contracts";
var daprHttpEndpoint = Environment.GetEnvironmentVariable("DAPR_HTTP_ENDPOINT") ?? "http://127.0.0.1:3500";
var bundle = Cp6ContractBundle.Load(contractRoot);
var cloudEventValidator = new Cp6CloudEventValidator(bundle);
var deliveryValidator = new Cp6DaprDeliveryValidator(cloudEventValidator);
var daprClient = new DaprClientBuilder().UseHttpEndpoint(daprHttpEndpoint).Build();
var invocationClient = new HttpClient { BaseAddress = new Uri(daprHttpEndpoint, UriKind.Absolute) };
var transport = new Cp6DaprTransport(daprClient, invocationClient);
var publisher = new Cp6DaprEventPublisher(transport, cloudEventValidator);
var invoker = new Cp6DaprServiceInvoker(transport);
var received = new ReceivedEventStore();

app.Lifetime.ApplicationStopped.Register(() =>
{
    activityListener.Dispose();
    invocationClient.Dispose();
    daprClient.Dispose();
});

app.MapGet("/healthz", () => Results.Ok(new { role }));

app.MapGet("/dapr/subscribe", () =>
    string.Equals(role, "receiver", StringComparison.Ordinal)
        ? Results.Json(new[]
        {
            new
            {
                pubsubname = Cp6DaprKafkaConventions.PubSubName,
                topic = "cp6.platform.contract-example-changed.v1",
                route = "/events/contract-example-changed"
            }
        })
        : Results.Json(Array.Empty<object>()));

app.MapPost("/publish-test", async (CancellationToken cancellationToken) =>
{
    var entry = bundle.Entries.Single();
    var example = entry.Examples.Single(candidate => candidate.Name == "valid");
    var body = await File.ReadAllBytesAsync(bundle.GetAssetPath(example.Path), cancellationToken);
    var receipt = await publisher.PublishAsync(body, cancellationToken);
    return Results.Json(receipt);
});

app.MapPost("/invoke-test", async (CancellationToken cancellationToken) =>
{
    using var content = JsonContent.Create(new { message = "p05-invocation" });
    using var response = await invoker.InvokeAsync(
        HttpMethod.Post,
        "cp6-p05-receiver",
        "invoke/echo",
        content,
        cancellationToken);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    return Results.Json(body);
});

app.MapPost("/invoke/echo", (JsonElement request) =>
    Results.Json(new
    {
        appId = "cp6-p05-receiver",
        message = request.GetProperty("message").GetString()
    }));

app.MapPost("/events/contract-example-changed", async (HttpContext context, CancellationToken cancellationToken) =>
{
    using var stream = new MemoryStream();
    await context.Request.Body.CopyToAsync(stream, cancellationToken);
    var topic = context.Request.Headers["__topic"].ToString();
    var partitionKey = context.Request.Headers["__key"].ToString();
    var result = deliveryValidator.Validate(stream.ToArray(), topic, partitionKey);
    if (!result.IsValid || result.CloudEvent is null)
    {
        return Results.Json(new { status = "DROP", failure = result.Failure });
    }

    received.Set(new ReceivedEventEvidence(
        result.CloudEvent.Id!,
        result.CloudEvent.Type!,
        topic,
        partitionKey,
        true));
    return Results.Json(new { status = "SUCCESS" });
});

app.MapGet("/events/last", () =>
{
    var value = received.Get();
    return value is null ? Results.NotFound() : Results.Json(value);
});

app.Run();

internal sealed class ReceivedEventStore
{
    private ReceivedEventEvidence? value;

    public ReceivedEventEvidence? Get() => Volatile.Read(ref value);

    public void Set(ReceivedEventEvidence evidence) => Volatile.Write(ref value, evidence);
}

internal sealed record ReceivedEventEvidence(
    string EventId,
    string EventType,
    string TopicName,
    string PartitionKey,
    bool ContractValid);
