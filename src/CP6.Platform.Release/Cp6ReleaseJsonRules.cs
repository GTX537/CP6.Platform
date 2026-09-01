using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

internal static class Cp6ReleaseJsonRules
{
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex GitSha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    internal static void RequireExactObject(JsonElement value, params string[] expectedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error("property-kind", "Expected a JSON object.");
        }

        var expected = expectedProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var property in expectedProperties)
        {
            if (!value.TryGetProperty(property, out _))
            {
                throw Error("missing-property", $"Required property '{property}' is missing.");
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name))
            {
                throw Error("unknown-property", $"Property '{property.Name}' is not allowed.");
            }
        }
    }

    internal static JsonElement RequireProperty(JsonElement value, string name, JsonValueKind kind)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property))
        {
            throw Error("missing-property", $"Required property '{name}' is missing.");
        }

        if (property.ValueKind != kind)
        {
            throw Error("property-kind", $"Property '{name}' must be {kind}.");
        }

        return property;
    }

    internal static string RequireString(JsonElement value, string name, string code)
    {
        var result = RequireProperty(value, name, JsonValueKind.String).GetString();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw Error(code, $"Property '{name}' must not be empty.");
        }

        return result;
    }

    internal static bool RequireBoolean(JsonElement value, string name, string code)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property))
        {
            throw Error("missing-property", $"Required property '{name}' is missing.");
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Error("property-kind", $"Property '{name}' must be a Boolean.");
        }

        return property.GetBoolean();
    }

    internal static long RequireNonNegativeInteger(JsonElement value, string name, string code)
    {
        var property = RequireProperty(value, name, JsonValueKind.Number);
        if (!property.TryGetInt64(out var result) || result < 0)
        {
            throw Error(code, $"Property '{name}' must be a non-negative Int64.");
        }

        return result;
    }

    internal static void RequireOrdinalSet(IReadOnlyList<string> values, string code)
    {
        if (values.Count != values.Distinct(StringComparer.Ordinal).Count() ||
            !values.SequenceEqual(values.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw Error(code, "Values must be unique and ordinal-sorted.");
        }
    }

    internal static void RequireSha256(string value, string code)
    {
        if (!Sha256.IsMatch(value)) throw Error(code, "Value is not a lowercase SHA-256.");
    }

    internal static void RequireGitSha(string value, string code)
    {
        if (!GitSha.IsMatch(value)) throw Error(code, "Value is not a lowercase Git SHA.");
    }

    internal static void RequireUtcMilliseconds(string value, string code)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _))
        {
            throw Error(code, "Value is not UTC with millisecond precision.");
        }
    }

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);
}
