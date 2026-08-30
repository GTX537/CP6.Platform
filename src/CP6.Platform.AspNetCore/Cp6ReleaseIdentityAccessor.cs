using CP6.Platform.Abstractions;
using CP6.Platform.Contracts;

namespace CP6.Platform.AspNetCore;

internal sealed class Cp6ReleaseIdentityAccessor(Cp6ReleaseIdentity current) : ICp6ReleaseIdentityAccessor
{
    public Cp6ReleaseIdentity Current { get; } = current;
}
