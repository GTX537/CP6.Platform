using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Platform.EntityFramework.TransportV1;

namespace CP6.Platform.WP0212.Tests;

public sealed class OutboxBridgeTests
{
    private static Cp6OutboxDispatchMessage Original(byte[] raw) => new(Guid.NewGuid(), "original-id", Guid.NewGuid(),
        "topic", "partition", raw, "correlation", "causation", "aggregate", 7, 3);

    [Fact]
    public async Task BridgeDefensivelyOwnsPayloadAndPreservesAllIdentities()
    {
        var raw = "raw-original"u8.ToArray(); var original = Original(raw); Cp6OutboxDispatchMessage? observed = null;
        var bridge = new TransportOutboxPublisherV1((message, _) =>
        {
            raw[0] = 0; observed = message;
            return ValueTask.FromResult(new OutboxTransportResultV2(OutboxTransportDecisionV2.Confirmed));
        });
        var result = await bridge.PublishAsync(original, default);
        Assert.Equal(OutboxTransportDecisionV2.Confirmed, result.Decision);
        Assert.Equal("raw-original"u8.ToArray(), observed!.Payload.ToArray());
        Assert.Equal(original with { Payload = observed.Payload }, observed);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task BridgeExceptionAndCancellationAreUnknown(bool cancel)
    {
        var bridge = new TransportOutboxPublisherV1((_, _) => throw new IOException("sensitive-error"));
        using var cts = new CancellationTokenSource(); if (cancel) cts.Cancel();
        var result = await bridge.PublishAsync(Original("raw"u8.ToArray()), cts.Token);
        Assert.Equal(OutboxTransportDecisionV2.Unknown, result.Decision);
        Assert.DoesNotContain("sensitive", result.SafeErrorCode ?? "");
    }

    [Fact]
    public async Task BridgeRejectsUnboundedPayloadBeforeCallingTransport()
    {
        var calls = 0;
        var bridge = new TransportOutboxPublisherV1((_, _) =>
        { calls++; return ValueTask.FromResult(new OutboxTransportResultV2(OutboxTransportDecisionV2.Confirmed)); });
        var result = await bridge.PublishAsync(Original(new byte[1048577]), default);
        Assert.Equal(OutboxTransportDecisionV2.DefinitelyRejected, result.Decision);
        Assert.Equal(0, calls);
    }
}
