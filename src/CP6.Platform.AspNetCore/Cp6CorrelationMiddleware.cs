using Microsoft.AspNetCore.Http;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// Establishes one safe correlation identifier and propagates it on the response.
/// </summary>
public sealed class Cp6CorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var incoming = httpContext.Request.Headers[HeaderName];
        var correlationId = Cp6CorrelationId.UseOrCreate(incoming.Count == 1 ? incoming[0] : null);

        httpContext.TraceIdentifier = correlationId;
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(httpContext);
    }
}
