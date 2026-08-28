using CP6.Platform.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// Establishes a validated request context and fails closed when none can be established.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        IRequestContextResolver resolver,
        IRequestContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(accessor);

        if (accessor is not RequestContextAccessor mutableAccessor)
        {
            throw new InvalidOperationException("The CP6 request context services were not registered correctly.");
        }

        try
        {
            var snapshot = await resolver.ResolveAsync(httpContext, httpContext.RequestAborted);
            if (snapshot is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            try
            {
                mutableAccessor.Set(new RequestContext(snapshot));
            }
            catch (ArgumentException)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(httpContext);
        }
        finally
        {
            mutableAccessor.Set(null);
        }
    }
}
