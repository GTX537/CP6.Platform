using CP6.Platform.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCore;

internal sealed class Cp6OutboundCorrelationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var httpContext = httpContextAccessor.HttpContext;
        var correlationId = Cp6CorrelationId.IsValid(httpContext?.TraceIdentifier)
            ? httpContext!.TraceIdentifier
            : RequestContextCorrelation(httpContext);
        correlationId = Cp6CorrelationId.UseOrCreate(correlationId);

        request.Headers.Remove(Cp6CorrelationMiddleware.HeaderName);
        request.Headers.TryAddWithoutValidation(Cp6CorrelationMiddleware.HeaderName, correlationId);
        return base.SendAsync(request, cancellationToken);
    }

    private static string? RequestContextCorrelation(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        var accessor = httpContext.RequestServices.GetService<IRequestContextAccessor>();
        var correlationId = accessor?.Current?.CorrelationId;
        return Cp6CorrelationId.IsValid(correlationId)
            ? correlationId
            : null;
    }
}
