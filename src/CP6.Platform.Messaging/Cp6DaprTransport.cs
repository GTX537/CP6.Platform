using Dapr.Client;

namespace CP6.Platform.Messaging;

/// <summary>
/// Uses the Dapr SDK for Pub/Sub and the stable HTTP service-invocation API.
/// The supplied HTTP client must address the local Dapr sidecar.
/// </summary>
public sealed class Cp6DaprTransport : ICp6DaprTransport
{
    private readonly DaprClient daprClient;
    private readonly HttpClient invocationClient;

    public Cp6DaprTransport(DaprClient daprClient, HttpClient invocationClient)
    {
        this.daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        this.invocationClient = invocationClient ?? throw new ArgumentNullException(nameof(invocationClient));
        if (invocationClient.BaseAddress is null || !invocationClient.BaseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("The invocation client must have an absolute Dapr HTTP endpoint.", nameof(invocationClient));
        }
    }

    public Task PublishAsync(
        string pubsubName,
        string topicName,
        ReadOnlyMemory<byte> body,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default) =>
        daprClient.PublishByteEventAsync(
            pubsubName,
            topicName,
            body,
            contentType,
            new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            cancellationToken);

    public Task<HttpResponseMessage> InvokeAsync(
        HttpMethod method,
        string appId,
        string methodName,
        HttpContent? content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        var target = $"v1.0/invoke/{Uri.EscapeDataString(appId)}/method/{methodName}";
        var request = new HttpRequestMessage(method, target) { Content = content };
        return invocationClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
