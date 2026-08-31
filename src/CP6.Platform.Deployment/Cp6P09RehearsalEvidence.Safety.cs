using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Deployment;

public sealed partial class Cp6P09RehearsalEvidence
{
    private static readonly Regex SummaryPattern = SafeRegex("^[A-Za-z0-9 ._-]{1,160}$");
    private static readonly Regex CredentialVocabularyPattern = new(
        "password|token|bearer|api-?key|secret|client-?secret|credential",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));
    private static readonly Regex CredentialPattern = new(
        "(?:password|token|connectionString)\\s*=\\s*\\S+|\\bBearer\\s+\\S{8,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));
    private static readonly Regex WindowsDrivePathPattern = new(
        "(?<![A-Za-z0-9])[A-Za-z]:[\\\\/]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex UncPathPattern = new(
        @"\\\\[^\\/\s]+[\\/][^\\/\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static void ValidateSafety(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateSafeString(property.Name);
                    if (property.Name.Equals("password", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("connectionString", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("secretValue", StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("unsafe-evidence", "Evidence contains a forbidden secret-bearing property name.");
                    }

                    ValidateSafety(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateSafety(item);
                }

                break;
            case JsonValueKind.String:
                ValidateSafeString(element.GetString()!);
                break;
        }
    }

    private static void ValidateSummary(string value)
    {
        if (!SummaryPattern.IsMatch(value) || CredentialVocabularyPattern.IsMatch(value))
        {
            Fail("unsafe-evidence", "Evidence summaries must use approved simple ASCII text without credential vocabulary.");
        }
    }

    private static void ValidateSafeString(string value)
    {
        if (value.Length == 0 ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\0', StringComparison.Ordinal) ||
            !value.IsNormalized(NormalizationForm.FormC))
        {
            Fail("invalid-string", "Evidence strings must be non-empty NFC without carriage returns or NUL characters.");
        }

        if (CredentialPattern.IsMatch(value) ||
            WindowsDrivePathPattern.IsMatch(value) ||
            UncPathPattern.IsMatch(value) ||
            ContainsAbsolutePathToken(value))
        {
            Fail("unsafe-evidence", "Evidence contains credential-like text or a machine-specific absolute path.");
        }
    }

    private static bool ContainsAbsolutePathToken(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '/' ||
                (index > 0 && !IsAbsolutePathDelimiter(value[index - 1])))
            {
                continue;
            }

            if (TryGetAllowedHttpUriEnd(value, index, out var uriEnd))
            {
                index = uriEnd - 1;
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsAbsolutePathDelimiter(char value) =>
        !char.IsLetterOrDigit(value) && value is not '.' and not '_' and not '/' and not '\\';

    private static bool TryGetAllowedHttpUriEnd(string value, int slashIndex, out int uriEnd)
    {
        uriEnd = slashIndex;
        if (slashIndex + 1 >= value.Length || value[slashIndex + 1] != '/')
        {
            return false;
        }

        var schemeStart = value.AsSpan(0, slashIndex).EndsWith("https:", StringComparison.OrdinalIgnoreCase)
            ? slashIndex - "https:".Length
            : value.AsSpan(0, slashIndex).EndsWith("http:", StringComparison.OrdinalIgnoreCase)
                ? slashIndex - "http:".Length
                : -1;
        if (schemeStart < 0 ||
            (schemeStart > 0 && !IsAbsolutePathDelimiter(value[schemeStart - 1])))
        {
            return false;
        }

        uriEnd = slashIndex + 2;
        while (uriEnd < value.Length && !char.IsWhiteSpace(value[uriEnd]))
        {
            uriEnd++;
        }

        var candidate = value[schemeStart..uriEnd];
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            !string.IsNullOrEmpty(uri.Host);
    }
}
