namespace CP6.Platform.Messaging;

/// <summary>
/// Applies the CP6 Dapr addressing profile before a service invocation reaches
/// the local sidecar. Authorization and business semantics remain service-owned.
/// </summary>
public sealed class Cp6DaprServiceInvoker
{
    private readonly ICp6DaprTransport transport;

    public Cp6DaprServiceInvoker(ICp6DaprTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<HttpResponseMessage> InvokeAsync(
        HttpMethod method,
        string appId,
        string methodName,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        Cp6DaprKafkaConventions.ValidateAppId(appId);
        Cp6DaprKafkaConventions.ValidateMethodName(methodName);

        using var operation = Cp6MessagingTelemetry.StartInvoke();
        try
        {
            var response = await transport.InvokeAsync(method, appId, methodName, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            operation.Success("invoked", Cp6MessagingTelemetry.MeasurementKind.None);
            return response;
        }
        catch (OperationCanceledException)
        {
            operation.Cancelled();
            throw;
        }
        catch (Exception)
        {
            operation.Failure("invocation_failure");
            throw;
        }
    }
}
