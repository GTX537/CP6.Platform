using CP6.Platform.Abstractions;
using CP6.Platform.Abstractions.Context;

namespace CP6.Platform.AspNetCore;

internal sealed class RequestContextAccessor(ContextScopeAccessorV1 scopes) : IRequestContextAccessor
{
    public IRequestContext? Current => scopes.Current;

    internal ContextScopeAccessorV1 Scopes => scopes;
}
