namespace CP6.Platform.Messaging;

/// <summary>
/// Isolates the CP6 messaging profile from the Dapr SDK so validation can be
/// tested without a sidecar and transport calls can remain the last step.
/// </summary>
public interface ICp6DaprTransport
{
    Task PublishAsync(
        string pubsubName,
        string topicName,
        ReadOnlyMemory<byte> body,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> InvokeAsync(
        HttpMethod method,
        string appId,
        string methodName,
        HttpContent? content,
        CancellationToken cancellationToken = default);
}
