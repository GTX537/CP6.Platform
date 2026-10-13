using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using Microsoft.AspNetCore.Http;

namespace CP6.Platform.AspNetCore.Context;

public sealed class TrustedRequestContextMiddlewareV1
{
    private readonly RequestDelegate _next;
    public TrustedRequestContextMiddlewareV1(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext, TrustedContextExecutorV1 executor,
        IContextSourceVerifierV1<HttpContext> verifier)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(verifier);
        var handlerStarted = false;
        try
        {
            await executor.ExecuteAsync(ContextEntryKindV1.Http, httpContext, verifier, _ =>
            {
                handlerStarted = true;
                return _next(httpContext);
            }, httpContext.RequestAborted);
        }
        catch (ContextResolutionExceptionV1 error) when (!handlerStarted && !httpContext.Response.HasStarted)
        {
            httpContext.Response.StatusCode = error.Failure switch
            {
                ContextResolutionFailureV1.AuthenticationRequired => StatusCodes.Status401Unauthorized,
                ContextResolutionFailureV1.AuthorityUnavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status403Forbidden
            };
        }
    }
}
