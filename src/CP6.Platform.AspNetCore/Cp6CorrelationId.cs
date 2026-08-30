using System.Text.RegularExpressions;

namespace CP6.Platform.AspNetCore;

internal static partial class Cp6CorrelationId
{
    internal static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && CorrelationPattern().IsMatch(value);

    internal static string UseOrCreate(string? value) =>
        IsValid(value) ? value! : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();
}
