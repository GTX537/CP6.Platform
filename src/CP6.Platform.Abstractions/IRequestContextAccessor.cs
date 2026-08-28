namespace CP6.Platform.Abstractions;

/// <summary>
/// Exposes the current request context without allowing consumers to replace it.
/// </summary>
public interface IRequestContextAccessor
{
    IRequestContext? Current { get; }
}
