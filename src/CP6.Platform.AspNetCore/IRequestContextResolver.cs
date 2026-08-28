using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Http;

namespace CP6.Platform.AspNetCore;

/// <summary>
/// Resolves request identity from a service-owned trusted authentication boundary.
/// </summary>
/// <remarks>
/// Implementations must not treat browser-controlled body, query, cookie, or external
/// tenant/user headers as authoritative identity input.
/// </remarks>
public interface IRequestContextResolver
{
    ValueTask<RequestContextSnapshot?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
