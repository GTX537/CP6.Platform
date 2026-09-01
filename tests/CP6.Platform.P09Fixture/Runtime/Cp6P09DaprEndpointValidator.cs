using System.Diagnostics.CodeAnalysis;
using System.Net;

internal enum Cp6P09DaprEndpointKind
{
    Http,
    Grpc
}

internal static class Cp6P09DaprEndpointValidator
{
    internal const string DefaultHttpEndpoint = "http://127.0.0.1:3500";
    internal const string DefaultGrpcEndpoint = "http://127.0.0.1:50001";

    private const int HttpPort = 3500;
    private const int GrpcPort = 50001;

    internal static bool TryParse(
        [NotNullWhen(true)] string? value,
        string role,
        Cp6P09DaprEndpointKind kind,
        [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrEmpty(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            candidate.Port != Port(kind) ||
            candidate.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            !IsRoleSidecarHost(candidate.IdnHost, role))
        {
            return false;
        }

        endpoint = candidate;
        return true;
    }

    private static int Port(Cp6P09DaprEndpointKind kind) => kind switch
    {
        Cp6P09DaprEndpointKind.Http => HttpPort,
        Cp6P09DaprEndpointKind.Grpc => GrpcPort,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Dapr endpoint kind.")
    };

    private static bool IsRoleSidecarHost(string host, string role)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return IPAddress.IsLoopback(address);
        }

        if (string.Equals(host, "localhost", StringComparison.Ordinal))
        {
            return true;
        }

        return role switch
        {
            "publisher" => string.Equals(host, "publisher-dapr", StringComparison.Ordinal),
            "receiver" => string.Equals(host, "receiver-dapr", StringComparison.Ordinal),
            "unauthorized" => string.Equals(host, "unauthorized-dapr", StringComparison.Ordinal),
            _ => false
        };
    }
}
