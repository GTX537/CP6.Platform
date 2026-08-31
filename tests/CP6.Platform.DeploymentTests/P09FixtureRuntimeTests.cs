using System.Diagnostics;
using System.Text.Json;
using Dapr;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CP6.Platform.DeploymentTests;

public sealed class P09FixtureRuntimeTests
{
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
    }

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

    private static DaprException DaprPublishException(StatusCode statusCode, string reason)
    {
        var rpcStatus = new Google.Rpc.Status
        {
            Code = (int)statusCode,
            Message = "bounded-test-status"
        };
        rpcStatus.Details.Add(Any.Pack(new Google.Rpc.ErrorInfo
        {
            Reason = reason,
            Domain = "dapr.io"
        }));
        var trailers = new Metadata
        {
            { "grpc-status-details-bin", rpcStatus.ToByteArray() }
        };
        return new DaprException(
            "Dapr publish failed.",
            new RpcException(new Status(statusCode, "bounded-test-status"), trailers));
    }
}
