namespace CP6.Platform.AspNetCore;

/// <summary>
/// Represents a fail-closed request validation or resilience failure with a stable category.
/// </summary>
public sealed class Cp6HttpResilienceException : Exception
{
    internal Cp6HttpResilienceException(Cp6HttpFailureCategory category, Exception? innerException = null)
        : base($"CP6 outbound HTTP failed with category '{category}'.", innerException)
    {
        Category = category;
    }

    public Cp6HttpFailureCategory Category { get; }
}
