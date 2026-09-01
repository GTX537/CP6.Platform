using Dapr;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

internal enum Cp6P09UnauthorizedPublishOutcome
{
    AllowedDenied,
    InfrastructureFailure,
    CallerCancellation
}

internal static class Cp6P09UnauthorizedPublishClassifier
{
    private const string RpcStatusDetailsTrailer = "grpc-status-details-bin";
    private const string DaprErrorDomain = "dapr.io";
    private const string PubSubNotFoundReason = "DAPR_PUBSUB_NOT_FOUND";
    private const string PubSubForbiddenReason = "DAPR_PUBSUB_FORBIDDEN";

    internal static Cp6P09UnauthorizedPublishOutcome Classify(
        Exception exception,
        bool callerCancellationRequested)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (callerCancellationRequested)
        {
            return Cp6P09UnauthorizedPublishOutcome.CallerCancellation;
        }

        if (exception is not DaprException { InnerException: RpcException rpcException })
        {
            return Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure;
        }

        var expectedReason = rpcException.StatusCode switch
        {
            StatusCode.InvalidArgument => PubSubNotFoundReason,
            StatusCode.PermissionDenied => PubSubForbiddenReason,
            _ => null
        };
        return expectedReason is not null && HasExactDaprReason(rpcException, expectedReason)
            ? Cp6P09UnauthorizedPublishOutcome.AllowedDenied
            : Cp6P09UnauthorizedPublishOutcome.InfrastructureFailure;
    }

    private static bool HasExactDaprReason(RpcException exception, string expectedReason)
    {
        var statusEntry = exception.Trailers.FirstOrDefault(entry =>
            entry.IsBinary && string.Equals(entry.Key, RpcStatusDetailsTrailer, StringComparison.Ordinal));
        if (statusEntry is null)
        {
            return false;
        }

        try
        {
            var rpcStatus = Google.Rpc.Status.Parser.ParseFrom(statusEntry.ValueBytes);
            if (rpcStatus.Code != (int)exception.StatusCode)
            {
                return false;
            }

            var errorInfo = rpcStatus.Details
                .Where(detail => detail.Is(Google.Rpc.ErrorInfo.Descriptor))
                .Select(detail => detail.Unpack<Google.Rpc.ErrorInfo>())
                .ToArray();
            return errorInfo.Length == 1 &&
                string.Equals(errorInfo[0].Domain, DaprErrorDomain, StringComparison.Ordinal) &&
                string.Equals(errorInfo[0].Reason, expectedReason, StringComparison.Ordinal);
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }
}
