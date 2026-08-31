using System.Diagnostics;

internal static class Cp6P09TraceTopology
{
    internal static bool TryCreateDelivery(
        ActivityContext publisher,
        ActivityContext receiver,
        ActivitySpanId receiverParentSpanId,
        out Cp6P09DeliveryTrace topology)
    {
        topology = default!;
        if (!IsObserved(publisher) ||
            !IsObserved(receiver) ||
            receiverParentSpanId == default ||
            publisher.TraceId != receiver.TraceId ||
            publisher.SpanId == receiver.SpanId ||
            publisher.SpanId != receiverParentSpanId)
        {
            return false;
        }

        topology = new Cp6P09DeliveryTrace(
            publisher.TraceId.ToHexString(),
            publisher.SpanId.ToHexString(),
            receiver.SpanId.ToHexString(),
            receiverParentSpanId.ToHexString());
        return true;
    }

    internal static bool TryCreateInvocation(
        ActivityContext publisherRequest,
        ActivityContext invoked,
        ActivitySpanId invokedParentSpanId,
        out Cp6P09InvocationTrace topology)
    {
        topology = default!;
        if (!IsObserved(publisherRequest) ||
            !IsObserved(invoked) ||
            invokedParentSpanId == default ||
            publisherRequest.TraceId != invoked.TraceId ||
            invokedParentSpanId == invoked.SpanId)
        {
            return false;
        }

        topology = new Cp6P09InvocationTrace(
            invoked.TraceId.ToHexString(),
            invokedParentSpanId.ToHexString(),
            invoked.SpanId.ToHexString(),
            invokedParentSpanId.ToHexString());
        return true;
    }

    internal static bool TryParseObservedContext(
        string? traceId,
        string? spanId,
        bool isRemote,
        out ActivityContext context)
    {
        context = default;
        if (!IsLowerHex(traceId, 32) || !IsLowerHex(spanId, 16))
        {
            return false;
        }

        var candidate = new ActivityContext(
            ActivityTraceId.CreateFromString(traceId.AsSpan()),
            ActivitySpanId.CreateFromString(spanId.AsSpan()),
            ActivityTraceFlags.Recorded,
            traceState: null,
            isRemote);
        if (!IsObserved(candidate))
        {
            return false;
        }

        context = candidate;
        return true;
    }

    internal static bool TryParseObservedSpanId(string? value, out ActivitySpanId spanId)
    {
        spanId = default;
        if (!IsLowerHex(value, 16))
        {
            return false;
        }

        var candidate = ActivitySpanId.CreateFromString(value.AsSpan());
        if (candidate == default)
        {
            return false;
        }

        spanId = candidate;
        return true;
    }

    private static bool IsObserved(ActivityContext context) =>
        context.TraceId != default && context.SpanId != default;

    private static bool IsLowerHex(string? value, int length) =>
        value is not null &&
        value.Length == length &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed record Cp6P09DeliveryTrace(
    string TraceId,
    string PublisherSpanId,
    string ReceiverSpanId,
    string ReceiverParentSpanId);

internal sealed record Cp6P09InvocationTrace(
    string InvocationTraceId,
    string InvokerSpanId,
    string InvokedSpanId,
    string InvokedParentSpanId);
