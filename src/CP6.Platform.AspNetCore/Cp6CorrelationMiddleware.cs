using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// Establishes one safe correlation identifier and propagates it on the response.
/// </summary>
public sealed partial class Cp6CorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var incoming = httpContext.Request.Headers[HeaderName];
        var correlationId = incoming.Count == 1 && CorrelationPattern().IsMatch(incoming[0] ?? string.Empty)
            ? incoming[0]!
            : Guid.NewGuid().ToString("N");

        httpContext.TraceIdentifier = correlationId;
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(httpContext);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();
}
