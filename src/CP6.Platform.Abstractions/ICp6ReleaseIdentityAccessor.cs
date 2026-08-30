using CP6.Platform.Contracts;

namespace CP6.Platform.Abstractions;

/// <summary>
/// Provides the validated immutable release identity of the current service.
/// </summary>
public interface ICp6ReleaseIdentityAccessor
{
    Cp6ReleaseIdentity Current { get; }
}
