using System.Net;
using CP6.Platform.Messaging;

internal enum Cp6P09ReceivedEvidenceProxyOutcome
{
    NotFound,
    BadGateway,
    Success
}

internal sealed record Cp6P09ReceivedEvidenceProxyResult(
    Cp6P09ReceivedEvidenceProxyOutcome Outcome,
    ReceivedEventEvidence? Evidence);

internal sealed class Cp6P09ReceivedEvidenceProxy
{
    private const int MaximumEvidenceBytes = 16_384;
    private const string ReceiverAppId = "cp6-p09-probe-receiver";
    private const string ReceivedMethodSegment = "received";
    private readonly ICp6DaprTransport transport;
    private readonly string expectedEventType;
    private readonly string expectedTopic;

    internal Cp6P09ReceivedEvidenceProxy(
        ICp6DaprTransport transport,
        string receiverAppId,
        string expectedEventType,
        string expectedTopic)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Cp6DaprKafkaConventions.ValidateAppId(receiverAppId);
        if (!string.Equals(receiverAppId, ReceiverAppId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The P09 receiver app id is not canonical.", nameof(receiverAppId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTopic);
        this.expectedEventType = expectedEventType;
        this.expectedTopic = expectedTopic;
    }

    internal async Task<Cp6P09ReceivedEvidenceProxyResult> GetAsync(
        PublishedProbeExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Cp6P09ProbeIdentifier.IsMethodSegment(expectation.EventId) ||
            !Cp6P09ProbeIdentifier.IsValid(expectation.PartitionKey))
        {
            return BadGateway();
        }

        var methodName = $"{ReceivedMethodSegment}/{expectation.EventId}";
        Cp6DaprKafkaConventions.ValidateMethodName(methodName);
        try
        {
            using var response = await transport.InvokeAsync(
                HttpMethod.Get,
                ReceiverAppId,
                methodName,
                content: null,
                cancellationToken);
            if (response is null)
            {
                return BadGateway();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new Cp6P09ReceivedEvidenceProxyResult(
                    Cp6P09ReceivedEvidenceProxyOutcome.NotFound,
                    null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return BadGateway();
            }

            var body = await ReadBoundedAsync(response.Content, cancellationToken);
            if (body is null ||
                !Cp6P09ReceivedEvidenceValidator.TryValidate(
                    body,
                    expectation.EventId,
                    expectation.PartitionKey,
                    expectedEventType,
                    expectedTopic,
                    out var evidence))
            {
                return BadGateway();
            }

            return new Cp6P09ReceivedEvidenceProxyResult(
                Cp6P09ReceivedEvidenceProxyOutcome.Success,
                evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BadGateway();
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is < 0 or > MaximumEvidenceBytes)
        {
            return null;
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(MaximumEvidenceBytes, (int)(content.Headers.ContentLength ?? 0)));
        var buffer = new byte[4 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumEvidenceBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static Cp6P09ReceivedEvidenceProxyResult BadGateway() =>
        new(Cp6P09ReceivedEvidenceProxyOutcome.BadGateway, null);
}
