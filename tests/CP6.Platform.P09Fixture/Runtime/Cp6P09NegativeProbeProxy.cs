using System.Net;
using System.Text.Json;
using CP6.Platform.Messaging;

internal enum Cp6P09NegativeProbeOutcome
{
    InvalidKind,
    Failed,
    Denied
}

internal sealed class Cp6P09NegativeProbeProxy
{
    private const int MaximumResponseBytes = 4096;
    private const string UnauthorizedAppId = "cp6-p09-unauthorized-probe";
    private readonly ICp6DaprTransport transport;

    internal Cp6P09NegativeProbeProxy(ICp6DaprTransport transport, string unauthorizedAppId)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Cp6DaprKafkaConventions.ValidateAppId(unauthorizedAppId);
        if (!string.Equals(unauthorizedAppId, UnauthorizedAppId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The P09 unauthorized AppId is not canonical.", nameof(unauthorizedAppId));
        }
    }

    internal async Task<Cp6P09NegativeProbeOutcome> InvokeAsync(
        string? kind,
        CancellationToken cancellationToken = default)
    {
        var request = kind switch
        {
            "direct-kafka" => new NegativeRequest(HttpMethod.Get, "direct-kafka", "direct-kafka-denied"),
            "appid-scope" => new NegativeRequest(HttpMethod.Post, "publish", "appid-scope-denied"),
            _ => null
        };
        if (request is null)
        {
            return Cp6P09NegativeProbeOutcome.InvalidKind;
        }

        try
        {
            using var response = await transport.InvokeAsync(
                request.Method,
                UnauthorizedAppId,
                request.MethodName,
                content: null,
                cancellationToken);
            if (response is null || response.StatusCode != HttpStatusCode.OK)
            {
                return Cp6P09NegativeProbeOutcome.Failed;
            }

            var payload = await ReadBoundedAsync(response.Content, cancellationToken);
            return payload is not null && IsExactDenial(payload, request.ExpectedCode)
                ? Cp6P09NegativeProbeOutcome.Denied
                : Cp6P09NegativeProbeOutcome.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Cp6P09NegativeProbeOutcome.Failed;
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is < 0 or > MaximumResponseBytes)
        {
            return null;
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumResponseBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool IsExactDenial(ReadOnlySpan<byte> payload, string expectedCode)
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            return names.Length == 2 &&
                names.Distinct(StringComparer.Ordinal).Count() == 2 &&
                names.Contains("denied", StringComparer.Ordinal) &&
                names.Contains("code", StringComparer.Ordinal) &&
                root.GetProperty("denied").ValueKind == JsonValueKind.True &&
                root.GetProperty("code").ValueKind == JsonValueKind.String &&
                string.Equals(root.GetProperty("code").GetString(), expectedCode, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record NegativeRequest(HttpMethod Method, string MethodName, string ExpectedCode);
}
