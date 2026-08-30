using System.Net;
using CP6.Platform.Messaging;

namespace CP6.Platform.UnitTests;

public sealed class DaprKafkaContractTests
{
    private static readonly string ContractRoot = FindContractRoot();

    [Fact]
    public void KafkaConventions_MapEventIdentityToCanonicalTopicAndPartitionKey()
    {
        var identity = Cp6EventContractIdentity.Parse("com.gtx537.crm.opportunity.order-requested.v1");
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        Assert.Equal("cp6.crm.opportunity-order-requested.v1", Cp6DaprKafkaConventions.GetTopic(identity));
        Assert.Equal(
            "11111111-1111-4111-8111-111111111111/opportunity-42",
            Cp6DaprKafkaConventions.GetPartitionKey(tenantId, "opportunity-42"));
    }

    [Theory]
    [InlineData("CRM")]
    [InlineData("crm-api")]
    [InlineData("cp6_CRM")]
    [InlineData("cp6-")]
    public void KafkaConventions_RejectInvalidDaprAppIds(string appId)
    {
        Assert.Throws<ArgumentException>(() => Cp6DaprKafkaConventions.ValidateAppId(appId));
    }

    [Theory]
    [InlineData("/api/customers")]
    [InlineData("api//customers")]
    [InlineData("api/../customers")]
    [InlineData("api/customers?tenant=hidden")]
    [InlineData("https://example.invalid/api")]
    public void KafkaConventions_RejectUnsafeInvocationPaths(string methodName)
    {
        Assert.Throws<ArgumentException>(() => Cp6DaprKafkaConventions.ValidateMethodName(methodName));
    }

    [Fact]
    public async Task Publisher_ValidatesBeforePublishingAndUsesCanonicalMetadata()
    {
        var transport = new RecordingTransport();
        var publisher = CreatePublisher(transport);
        var body = LoadExample("valid");

        var receipt = await publisher.PublishAsync(body);

        var call = Assert.Single(transport.PublishCalls);
        Assert.Equal(Cp6DaprKafkaConventions.PubSubName, call.PubSubName);
        Assert.Equal("cp6.platform.contract-example-changed.v1", call.TopicName);
        Assert.Equal(Cp6CloudEventCodec.StructuredContentType, call.ContentType);
        Assert.True(body.Span.SequenceEqual(call.Body.Span));
        Assert.Equal(
            "11111111-1111-4111-8111-111111111111/example-1",
            call.Metadata[Cp6DaprKafkaConventions.PartitionKeyMetadata]);
        Assert.Equal(call.TopicName, receipt.TopicName);
        Assert.Equal("evt-0001", receipt.EventId);
    }

    [Fact]
    public async Task Publisher_InvalidContractNeverReachesTransport()
    {
        var transport = new RecordingTransport();
        var publisher = CreatePublisher(transport);

        var exception = await Assert.ThrowsAsync<Cp6DaprContractException>(
            () => publisher.PublishAsync(LoadExample("wrong-type")));

        Assert.Equal(Cp6DaprContractFailure.EventContractInvalid, exception.Failure);
        Assert.Empty(transport.PublishCalls);
    }

    [Fact]
    public void DeliveryValidator_FailsClosedOnTopicOrPartitionDrift()
    {
        var validator = CreateDeliveryValidator();
        var body = LoadExample("valid");
        const string topic = "cp6.platform.contract-example-changed.v1";
        const string key = "11111111-1111-4111-8111-111111111111/example-1";

        var valid = validator.Validate(body, topic, key);
        var wrongTopic = validator.Validate(body, "cp6.crm.contract-example-changed.v1", key);
        var wrongKey = validator.Validate(body, topic, "11111111-1111-4111-8111-111111111111/example-2");

        Assert.True(valid.IsValid);
        Assert.Equal("evt-0001", valid.CloudEvent!.Id);
        Assert.Null(valid.ParentContext);
        Assert.Equal(Cp6DaprContractFailure.TopicMismatch, wrongTopic.Failure);
        Assert.Null(wrongTopic.CloudEvent);
        Assert.Equal(Cp6DaprContractFailure.PartitionKeyMismatch, wrongKey.Failure);
        Assert.Null(wrongKey.CloudEvent);
    }

    [Fact]
    public async Task ServiceInvoker_ValidatesAddressBeforeTransportAndReturnsSuccessfulResponse()
    {
        var transport = new RecordingTransport();
        var invoker = new Cp6DaprServiceInvoker(transport);

        using var response = await invoker.InvokeAsync(HttpMethod.Post, "cp6-crm-api", "api/validation/check");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(transport.InvocationCalls);
        Assert.Equal("cp6-crm-api", call.AppId);
        Assert.Equal("api/validation/check", call.MethodName);

        await Assert.ThrowsAsync<ArgumentException>(
            () => invoker.InvokeAsync(HttpMethod.Get, "crm-api", "api/customers"));
        Assert.Single(transport.InvocationCalls);
    }

    private static Cp6DaprEventPublisher CreatePublisher(ICp6DaprTransport transport) =>
        new(transport, new Cp6CloudEventValidator(Cp6ContractBundle.Load(ContractRoot)));

    private static Cp6DaprDeliveryValidator CreateDeliveryValidator() =>
        new(new Cp6CloudEventValidator(Cp6ContractBundle.Load(ContractRoot)));

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

    private sealed class RecordingTransport : ICp6DaprTransport
    {
        public List<PublishCall> PublishCalls { get; } = [];

        public List<InvocationCall> InvocationCalls { get; } = [];

        public Task PublishAsync(
            string pubsubName,
            string topicName,
            ReadOnlyMemory<byte> body,
            string contentType,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default)
        {
            PublishCalls.Add(new(pubsubName, topicName, body.ToArray(), contentType, metadata));
            return Task.CompletedTask;
        }

        public Task<HttpResponseMessage> InvokeAsync(
            HttpMethod method,
            string appId,
            string methodName,
            HttpContent? content,
            CancellationToken cancellationToken = default)
        {
            InvocationCalls.Add(new(method, appId, methodName));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed record PublishCall(
        string PubSubName,
        string TopicName,
        ReadOnlyMemory<byte> Body,
        string ContentType,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record InvocationCall(HttpMethod Method, string AppId, string MethodName);
}
