using CP6.Platform.Abstractions;

namespace CP6.Platform.AspNetCore;

internal sealed class RequestContextAccessor : IRequestContextAccessor
{
    public IRequestContext? Current { get; private set; }

    internal void Set(IRequestContext? context) => Current = context;
}
