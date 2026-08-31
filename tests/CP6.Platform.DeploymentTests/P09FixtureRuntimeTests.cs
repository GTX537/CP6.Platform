using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Platform.Messaging;
using Dapr;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CP6.Platform.DeploymentTests;

public sealed class P09FixtureRuntimeTests
{
    private const string ValidReceivedEvidenceJson =
        "{\"eventId\":\"p09-event-0001\",\"eventType\":\"com.gtx537.platform.contract-example.changed.v1\",\"topicName\":\"cp6.platform.deployment-probe.v1\",\"partitionKey\":\"cp6-p09-entity-0001\",\"region\":\"TEST\",\"traceId\":\"11111111111111111111111111111111\",\"publisherSpanId\":\"2222222222222222\",\"receiverSpanId\":\"3333333333333333\",\"receiverParentSpanId\":\"2222222222222222\",\"contractValid\":true}";

    [Fact]
    public async Task ReceivedEvidenceProxy_DisposesRepeatedNotFoundAndOtherNonSuccessResponses()
    {
        var contents = Enumerable.Range(0, 3)
            .Select(_ => new TrackingHttpContent([]))
            .ToArray();
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.NotFound) { Content = contents[0] },
            new(HttpStatusCode.NotFound) { Content = contents[1] },
            new(HttpStatusCode.ServiceUnavailable) { Content = contents[2] }
        ]);
        var transport = new RecordingDaprTransport((_, _, _, _, _) => Task.FromResult(responses.Dequeue()));
        var proxy = CreateReceivedEvidenceProxy(transport);

        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.NotFound, (await proxy.GetAsync(Expectation())).Outcome);
        Assert.True(contents[0].IsDisposed);
        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.NotFound, (await proxy.GetAsync(Expectation())).Outcome);
        Assert.True(contents[1].IsDisposed);
        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, (await proxy.GetAsync(Expectation())).Outcome);
        Assert.True(contents[2].IsDisposed);
    }

    [Fact]
    public async Task ReceivedEvidenceProxy_UsesFixedTargetValidatesSuccessAndDisposesResponse()
    {
        var content = new TrackingHttpContent(Encoding.UTF8.GetBytes(ValidReceivedEvidenceJson));
        var transport = new RecordingDaprTransport((_, _, _, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var proxy = CreateReceivedEvidenceProxy(transport);

        var result = await proxy.GetAsync(Expectation());

        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.Success, result.Outcome);
        Assert.NotNull(result.Evidence);
        Assert.Equal("p09-event-0001", result.Evidence.EventId);
        Assert.True(content.IsDisposed);
        var call = Assert.Single(transport.Invocations);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("cp6-p09-probe-receiver", call.AppId);
        Assert.Equal("received/p09-event-0001", call.MethodName);
        Assert.Null(call.Content);
    }

    [Fact]
    public async Task ReceivedEvidenceProxy_RejectsArbitraryPathBeforeTransport()
    {
        var transport = new RecordingDaprTransport((_, _, _, _, _) => throw new InvalidOperationException());
        var proxy = CreateReceivedEvidenceProxy(transport);

        var result = await proxy.GetAsync(new PublishedProbeExpectation("../escape", "cp6-p09-entity-0001"));

        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, result.Outcome);
        Assert.Empty(transport.Invocations);
        Assert.Throws<ArgumentException>(() => new Cp6P09ReceivedEvidenceProxy(
            transport,
            "receiver.example",
            "com.gtx537.platform.contract-example.changed.v1",
            "cp6.platform.deployment-probe.v1"));
    }

    [Fact]
    public async Task ReceivedEvidenceProxy_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new RecordingDaprTransport((_, _, _, _, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        var proxy = CreateReceivedEvidenceProxy(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            proxy.GetAsync(Expectation(), cancellation.Token));

        Assert.Equal(cancellation.Token, Assert.Single(transport.Invocations).CancellationToken);
    }

    [Fact]
    public async Task ReceivedEvidenceProxy_DisposesResponseWhenContentReadThrows()
    {
        var stream = new TrackingReadStream(_ =>
            ValueTask.FromException<int>(new IOException("sensitive read detail")));
        var content = new TrackingStreamContent(stream);
        var transport = new RecordingDaprTransport((_, _, _, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var proxy = CreateReceivedEvidenceProxy(transport);

        var result = await proxy.GetAsync(Expectation());

        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task ReceivedEvidenceProxy_DisposesResponseWhenContentReadIsCallerCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new TrackingReadStream(token =>
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(token);
        });
        var content = new TrackingStreamContent(stream);
        var transport = new RecordingDaprTransport((_, _, _, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var proxy = CreateReceivedEvidenceProxy(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            proxy.GetAsync(Expectation(), cancellation.Token));

        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task ReceivedEvidenceProxy_MapsTransportMalformedAndOversizedFailuresWithoutDetails()
    {
        var contents = new[]
        {
            new TrackingHttpContent(Encoding.UTF8.GetBytes("not-json")),
            new TrackingHttpContent(new byte[16_385])
        };
        var responses = new Queue<HttpResponseMessage>(contents.Select(content =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var call = 0;
        var transport = new RecordingDaprTransport((_, _, _, _, _) =>
        {
            call++;
            return call <= 2
                ? Task.FromResult(responses.Dequeue())
                : Task.FromException<HttpResponseMessage>(new HttpRequestException("sensitive transport detail"));
        });
        var proxy = CreateReceivedEvidenceProxy(transport);

        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, (await proxy.GetAsync(Expectation())).Outcome);
        Assert.True(contents[0].IsDisposed);
        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, (await proxy.GetAsync(Expectation())).Outcome);
        Assert.True(contents[1].IsDisposed);
        var transportFailure = await proxy.GetAsync(Expectation());
        Assert.Equal(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, transportFailure.Outcome);
        Assert.Null(transportFailure.Evidence);
    }

    [Theory]
    [InlineData("publisher", "Http", "http://127.0.0.1:3500")]
    [InlineData("publisher", "Http", "http://publisher-dapr:3500")]
    [InlineData("publisher", "Grpc", "http://127.0.0.1:50001")]
    [InlineData("publisher", "Grpc", "http://publisher-dapr:50001")]
    [InlineData("unauthorized", "Http", "http://unauthorized-dapr:3500")]
    [InlineData("unauthorized", "Grpc", "http://unauthorized-dapr:50001")]
    public void DaprEndpointValidator_AllowsOnlyOwnSidecarOrLoopback(
        string role,
        string kind,
        string value)
    {
        Assert.True(Cp6P09DaprEndpointValidator.TryParse(
            value,
            role,
            System.Enum.Parse<Cp6P09DaprEndpointKind>(kind),
            out var endpoint));
        Assert.Equal(value, endpoint.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("publisher", "Http", "http://example.test:3500")]
    [InlineData("publisher", "Http", "http://unauthorized-dapr:3500")]
    [InlineData("unauthorized", "Http", "http://publisher-dapr:3500")]
    [InlineData("publisher", "Http", "http://publisher-dapr:3501")]
    [InlineData("publisher", "Grpc", "http://publisher-dapr:50002")]
    [InlineData("publisher", "Grpc", "https://publisher-dapr:50001")]
    [InlineData("publisher", "Grpc", "http://user@publisher-dapr:50001")]
    [InlineData("publisher", "Grpc", "http://publisher-dapr:50001/path")]
    [InlineData("publisher", "Grpc", "http://publisher-dapr:50001/?query=1")]
    [InlineData("publisher", "Grpc", "http://publisher-dapr:50001/#fragment")]
    public void DaprEndpointValidator_RejectsCrossRoleAndNonSidecarEndpoints(
        string role,
        string kind,
        string value)
    {
        Assert.False(Cp6P09DaprEndpointValidator.TryParse(
            value,
            role,
            System.Enum.Parse<Cp6P09DaprEndpointKind>(kind),
            out _));
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument, "DAPR_PUBSUB_NOT_FOUND")]
    [InlineData(StatusCode.PermissionDenied, "DAPR_PUBSUB_FORBIDDEN")]
    public void UnauthorizedPublishClassifier_AllowsOnlyProvenDenialStatuses(
        StatusCode statusCode,
        string reason)
    {
        var exception = DaprPublishException(statusCode, reason);

        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.AllowedDenied,
            Cp6P09UnauthorizedPublishClassifier.Classify(exception, callerCancellationRequested: false));
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument, "DAPR_PUBSUB_METADATA_DESERIALIZATION")]
    [InlineData(StatusCode.PermissionDenied, "DAPR_SECRET_PERMISSION_DENIED")]
    [InlineData(StatusCode.NotFound, "DAPR_PUBSUB_NOT_FOUND")]
    public void UnauthorizedPublishClassifier_RequiresExactStatusAndDaprReason(
        StatusCode statusCode,
        string reason)
    {
        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                DaprPublishException(statusCode, reason),
                callerCancellationRequested: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("DAPR.IO")]
    [InlineData("Dapr.io")]
    public void UnauthorizedPublishClassifier_RequiresExactDaprDomain(string domain)
    {
        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                DaprPublishException(
                    StatusCode.PermissionDenied,
                    "DAPR_PUBSUB_FORBIDDEN",
                    domain),
                callerCancellationRequested: false));
    }

    [Fact]
    public void UnauthorizedPublishClassifier_RejectsDuplicateMatchingErrorInfo()
    {
        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                DaprPublishException(
                    StatusCode.PermissionDenied,
                    "DAPR_PUBSUB_FORBIDDEN",
                    "dapr.io",
                    duplicateErrorInfo: true),
                callerCancellationRequested: false));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Cancelled)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.Unknown)]
    public void UnauthorizedPublishClassifier_RejectsInfrastructureAndMalformedFailures(StatusCode statusCode)
    {
        var exception = new DaprException(
            "Dapr publish failed.",
            new RpcException(new Status(statusCode, "bounded-test-status")));

        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(exception, callerCancellationRequested: false));
    }

    [Fact]
    public void UnauthorizedPublishClassifier_DoesNotTreatArbitraryExceptionsAsDenial()
    {
        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                new InvalidOperationException("malformed component"),
                callerCancellationRequested: false));
    }

    [Fact]
    public void UnauthorizedPublishClassifier_RequiresTheDaprSdkWrapperForRpcDenial()
    {
        var rpcException = new RpcException(new Status(StatusCode.NotFound, "bounded-test-status"));

        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                new InvalidOperationException("not a Dapr publish failure", rpcException),
                callerCancellationRequested: false));
    }

    [Fact]
    public void UnauthorizedPublishClassifier_PreservesCallerCancellation()
    {
        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.CallerCancellation,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                new OperationCanceledException(),
                callerCancellationRequested: true));

        Assert.Equal(
            Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure,
            Cp6P09UnauthorizedPublishClassifier.Classify(
                new OperationCanceledException(),
                callerCancellationRequested: false));
    }

    [Fact]
    public void TraceTopology_AcceptsObservedDirectParentChildSpansAndSerializesEvidenceNames()
    {
        var eventTraceId = ActivityTraceId.CreateFromString("11111111111111111111111111111111");
        var publisherSpanId = ActivitySpanId.CreateFromString("2222222222222222");
        var receiverSpanId = ActivitySpanId.CreateFromString("3333333333333333");
        var publisher = Context(eventTraceId, publisherSpanId);
        var receiver = Context(eventTraceId, receiverSpanId, isRemote: true);

        Assert.True(Cp6P09TraceTopology.TryCreateDelivery(
            publisher,
            receiver,
            publisherSpanId,
            out var delivery));

        Assert.Equal("11111111111111111111111111111111", delivery.TraceId);
        Assert.Equal("2222222222222222", delivery.PublisherSpanId);
        Assert.Equal("3333333333333333", delivery.ReceiverSpanId);
        Assert.Equal("2222222222222222", delivery.ReceiverParentSpanId);
        Assert.Equal(
            new[] { "publisherSpanId", "receiverParentSpanId", "receiverSpanId", "traceId" },
            JsonPropertyNames(delivery));

        var invocationTraceId = ActivityTraceId.CreateFromString("44444444444444444444444444444444");
        var invokerSpanId = ActivitySpanId.CreateFromString("5555555555555555");
        var invokedSpanId = ActivitySpanId.CreateFromString("6666666666666666");
        var invoker = Context(invocationTraceId, invokerSpanId);
        var invoked = Context(invocationTraceId, invokedSpanId, isRemote: true);

        Assert.True(Cp6P09TraceTopology.TryCreateInvocation(
            invoker,
            invoked,
            invokerSpanId,
            out var invocation));

        Assert.Equal("44444444444444444444444444444444", invocation.InvocationTraceId);
        Assert.Equal("5555555555555555", invocation.InvokerSpanId);
        Assert.Equal("6666666666666666", invocation.InvokedSpanId);
        Assert.Equal("5555555555555555", invocation.InvokedParentSpanId);
        Assert.Equal(
            new[] { "invocationTraceId", "invokedParentSpanId", "invokedSpanId", "invokerSpanId" },
            JsonPropertyNames(invocation));
    }

    [Fact]
    public void PublishReceipt_SerializesWithoutAmbiguousTraceEvidence()
    {
        var receipt = new PublishProbeReceipt(
            "p09-event-0001",
            "cp6-p09-entity-0001",
            "TEST",
            "cp6.platform.deployment-probe.v1",
            "cp6-p09-kafka-publish");

        Assert.Equal(
            new[] { "component", "eventId", "partitionKey", "region", "topic" },
            JsonPropertyNames(receipt));
    }

    [Fact]
    public void ReceivedEvidenceValidator_AcceptsOnlyTheExpectedCanonicalObservation()
    {
        var utf8 = Encoding.UTF8.GetBytes(
            ValidReceivedEvidenceJson);

        Assert.True(Cp6P09ReceivedEvidenceValidator.TryValidate(
            utf8,
            "p09-event-0001",
            "cp6-p09-entity-0001",
            "com.gtx537.platform.contract-example.changed.v1",
            "cp6.platform.deployment-probe.v1",
            out var evidence));
        Assert.NotNull(evidence);
        Assert.Equal("p09-event-0001", evidence.EventId);
        Assert.Equal("2222222222222222", evidence.ReceiverParentSpanId);
        Assert.True(evidence.ContractValid);
    }

    [Theory]
    [MemberData(nameof(InvalidReceivedEvidenceJson))]
    public void ReceivedEvidenceValidator_RejectsMismatchMalformedAndAmbiguousPayloads(string json)
    {
        Assert.False(Cp6P09ReceivedEvidenceValidator.TryValidate(
            Encoding.UTF8.GetBytes(json),
            "p09-event-0001",
            "cp6-p09-entity-0001",
            "com.gtx537.platform.contract-example.changed.v1",
            "cp6.platform.deployment-probe.v1",
            out var evidence));
        Assert.Null(evidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".hidden")]
    [InlineData("../escape")]
    [InlineData("event/child")]
    [InlineData("event%2fchild")]
    [InlineData("event?query")]
    [InlineData("event#fragment")]
    [InlineData("event\\child")]
    [InlineData("event:child")]
    [InlineData("Event-child")]
    [InlineData(" event")]
    [InlineData("évent")]
    public void ProbeIdentifier_RejectsUnsafeMethodPathSegments(string value) =>
        Assert.False(Cp6P09ProbeIdentifier.IsMethodSegment(value));

    [Theory]
    [InlineData("p09-event-0001")]
    [InlineData("a")]
    [InlineData("a_1.two-three")]
    public void ProbeIdentifier_AllowsOnlyCanonicalAsciiMethodPathSegments(string value) =>
        Assert.True(Cp6P09ProbeIdentifier.IsMethodSegment(value));

    public static TheoryData<string> InvalidReceivedEvidenceJson()
    {
        var values = new TheoryData<string>
        {
            "not-json",
            "{}",
            ValidReceivedEvidenceJson.Replace("p09-event-0001", "p09-event-9999", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("cp6-p09-entity-0001", "cp6-p09-entity-9999", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("com.gtx537.platform.contract-example.changed.v1", "com.gtx537.platform.other.v1", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("cp6.platform.deployment-probe.v1", "cp6.platform.other.v1", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"TEST\"", "\"test\"", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"contractValid\":true", "\"contractValid\":false", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("11111111111111111111111111111111", "00000000000000000000000000000000", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("11111111111111111111111111111111", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("3333333333333333", "2222222222222222", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"receiverParentSpanId\":\"2222222222222222\"", "\"receiverParentSpanId\":\"4444444444444444\"", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"contractValid\":true", "\"contractValid\":true,\"extra\":true", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("{\"eventId\":", "{\"eventId\":\"p09-event-0001\",\"eventId\":", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"topicName\":\"cp6.platform.deployment-probe.v1\",", string.Empty, StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"contractValid\":true", "\"contractValid\":\"true\"", StringComparison.Ordinal),
            ValidReceivedEvidenceJson.Replace("\"eventId\"", "\"EventId\"", StringComparison.Ordinal)
        };
        return values;
    }

    [Fact]
    public void TraceTopology_RejectsSyntheticOrDiscontinuousRelationships()
    {
        var firstTraceId = ActivityTraceId.CreateFromString("11111111111111111111111111111111");
        var secondTraceId = ActivityTraceId.CreateFromString("44444444444444444444444444444444");
        var parentSpanId = ActivitySpanId.CreateFromString("2222222222222222");
        var childSpanId = ActivitySpanId.CreateFromString("3333333333333333");
        var wrongParentSpanId = ActivitySpanId.CreateFromString("7777777777777777");

        Assert.False(Cp6P09TraceTopology.TryCreateDelivery(
            Context(firstTraceId, parentSpanId),
            Context(secondTraceId, childSpanId),
            parentSpanId,
            out _));
        Assert.False(Cp6P09TraceTopology.TryCreateDelivery(
            Context(firstTraceId, parentSpanId),
            Context(firstTraceId, childSpanId),
            wrongParentSpanId,
            out _));
        Assert.False(Cp6P09TraceTopology.TryCreateInvocation(
            Context(firstTraceId, parentSpanId),
            Context(firstTraceId, parentSpanId),
            parentSpanId,
            out _));
        Assert.False(Cp6P09TraceTopology.TryCreateInvocation(
            Context(firstTraceId, parentSpanId),
            Context(secondTraceId, childSpanId),
            parentSpanId,
            out _));
        Assert.False(Cp6P09TraceTopology.TryCreateInvocation(
            Context(firstTraceId, parentSpanId),
            Context(firstTraceId, childSpanId),
            default,
            out _));
    }

    [Fact]
    public void TraceTopology_UsesReceiverObservedOutboundTransportSpanAsInvokerEvidence()
    {
        using var publisherRequest = new Activity("publisher-request")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        using var outboundRequest = new Activity("http-outbound")
            .SetParentId(publisherRequest.Id!)
            .Start();
        var outboundSpanId = outboundRequest.SpanId;
        var outboundTraceParent = outboundRequest.Id!;
        outboundRequest.Stop();
        using var invokedRequest = new Activity("receiver-request")
            .SetParentId(outboundTraceParent)
            .Start();

        Assert.True(Cp6P09TraceTopology.TryCreateInvocation(
            publisherRequest.Context,
            invokedRequest.Context,
            invokedRequest.ParentSpanId,
            out var invocation));
        Assert.Equal(publisherRequest.TraceId.ToHexString(), invocation.InvocationTraceId);
        Assert.Equal(outboundSpanId.ToHexString(), invocation.InvokerSpanId);
        Assert.Equal(invokedRequest.SpanId.ToHexString(), invocation.InvokedSpanId);
        Assert.Equal(outboundSpanId.ToHexString(), invocation.InvokedParentSpanId);
        Assert.NotEqual(publisherRequest.SpanId.ToHexString(), invocation.InvokerSpanId);
    }

    private static Cp6P09ReceivedEvidenceProxy CreateReceivedEvidenceProxy(ICp6DaprTransport transport) =>
        new(
            transport,
            "cp6-p09-probe-receiver",
            "com.gtx537.platform.contract-example.changed.v1",
            "cp6.platform.deployment-probe.v1");

    private static PublishedProbeExpectation Expectation() =>
        new("p09-event-0001", "cp6-p09-entity-0001");

    private static ActivityContext Context(
        ActivityTraceId traceId,
        ActivitySpanId spanId,
        bool isRemote = false) =>
        new(traceId, spanId, ActivityTraceFlags.Recorded, traceState: null, isRemote);

    private static string[] JsonPropertyNames<T>(T value)
    {
        var json = JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return json.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static DaprException DaprPublishException(
        StatusCode statusCode,
        string reason,
        string domain = "dapr.io",
        bool duplicateErrorInfo = false)
    {
        var rpcStatus = new Google.Rpc.Status
        {
            Code = (int)statusCode,
            Message = "bounded-test-status"
        };
        rpcStatus.Details.Add(Any.Pack(new Google.Rpc.ErrorInfo
        {
            Reason = reason,
            Domain = domain
        }));
        if (duplicateErrorInfo)
        {
            rpcStatus.Details.Add(Any.Pack(new Google.Rpc.ErrorInfo
            {
                Reason = reason,
                Domain = domain
            }));
        }

        rpcStatus.Details.Add(Any.Pack(new Google.Rpc.ResourceInfo
        {
            ResourceType = "pubsub",
            ResourceName = "cp6-p09-kafka-publish"
        }));
        var trailers = new Metadata
        {
            { "grpc-status-details-bin", rpcStatus.ToByteArray() }
        };
        return new DaprException(
            "Dapr publish failed.",
            new RpcException(new Status(statusCode, "bounded-test-status"), trailers));
    }

    private sealed class RecordingDaprTransport : ICp6DaprTransport
    {
        private readonly Func<
            HttpMethod,
            string,
            string,
            HttpContent?,
            CancellationToken,
            Task<HttpResponseMessage>> invoke;

        internal RecordingDaprTransport(Func<
            HttpMethod,
            string,
            string,
            HttpContent?,
            CancellationToken,
            Task<HttpResponseMessage>> invoke) => this.invoke = invoke;

        internal List<TransportInvocation> Invocations { get; } = [];

        public Task<HttpResponseMessage> InvokeAsync(
            HttpMethod method,
            string appId,
            string methodName,
            HttpContent? content,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(new TransportInvocation(method, appId, methodName, content, cancellationToken));
            return invoke(method, appId, methodName, content, cancellationToken);
        }

        public Task PublishAsync(
            string pubsubName,
            string topicName,
            ReadOnlyMemory<byte> body,
            string contentType,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record TransportInvocation(
        HttpMethod Method,
        string AppId,
        string MethodName,
        HttpContent? Content,
        CancellationToken CancellationToken);

    private sealed class TrackingHttpContent : HttpContent
    {
        private readonly byte[] payload;

        internal TrackingHttpContent(byte[] payload) => this.payload = payload;

        internal bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = payload.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingReadStream : Stream
    {
        private readonly Func<CancellationToken, ValueTask<int>> read;

        internal TrackingReadStream(Func<CancellationToken, ValueTask<int>> read) => this.read = read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => read(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    }

    private sealed class TrackingStreamContent : HttpContent
    {
        private readonly Stream stream;

        internal TrackingStreamContent(Stream stream) => this.stream = stream;

        internal bool IsDisposed { get; private set; }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult(stream);

        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) =>
            stream.CopyToAsync(target);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            if (disposing)
            {
                stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
