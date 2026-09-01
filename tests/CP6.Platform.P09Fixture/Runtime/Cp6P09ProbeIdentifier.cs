using System.Diagnostics.CodeAnalysis;

internal static class Cp6P09ProbeIdentifier
{
    internal static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (value is not { Length: >= 1 and <= 128 } || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        return value.AsSpan(1).IndexOfAnyExcept(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._:-") < 0;
    }

    internal static bool IsMethodSegment([NotNullWhen(true)] string? value)
    {
        if (value is not { Length: >= 1 and <= 128 } ||
            value[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return false;
        }

        return value.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789._-") < 0;
    }
}
