using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CP6.Platform.Messaging.NativeContracts;

/// <summary>Strict native JSON and schema-value semantics. Equality is never a digest or replay codec.</summary>
public static class Cp6NativeJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static JsonDocument Parse(ReadOnlyMemory<byte> utf8) => ParseCore(utf8, false);

    // P08 discards duplicate root trace context without rejecting a business message.
    // This internal exception never applies to native JSON or nested business data.
    internal static JsonDocument ParseCloudEvent(ReadOnlyMemory<byte> utf8) => ParseCore(utf8, true);

    private static JsonDocument ParseCore(ReadOnlyMemory<byte> utf8, bool allowDuplicateTraceContext)
    {
        // Own the input so later caller mutations cannot change the parsed document.
        var owned = utf8.ToArray();
        try { StrictUtf8.GetCharCount(owned); }
        catch (DecoderFallbackException) { throw new JsonException("JSON contains invalid UTF-8."); }
        var document = JsonDocument.Parse(owned, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64
        });
        try
        {
            CheckProperties(document.RootElement, allowDuplicateTraceContext);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    public static void RequireNullProperty(ReadOnlyMemory<byte> utf8, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        using var document = Parse(utf8);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Null)
            throw new JsonException("A required null property is missing or has a non-null value.");
    }

    public static bool SemanticallyEquals(ReadOnlyMemory<byte> left, ReadOnlyMemory<byte> right)
    {
        using var leftDocument = Parse(left);
        using var rightDocument = Parse(right);
        return Key(leftDocument.RootElement) == Key(rightDocument.RootElement);
    }

    private static void CheckProperties(JsonElement value, bool allowDuplicateTraceContext = false)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) &&
                    !(allowDuplicateTraceContext && property.Name is "traceparent" or "tracestate"))
                    throw new JsonException("JSON contains a duplicate property.");
                CheckProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckProperties(item);
    }

    // Adopted from pinned DesignRevisionTypes.cs; provenance is in S01-09/PLAN.md.
    // Keys stay private: semantic equality must not become a wire normalization API.
    private static string Key(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "O{" + string.Join(",", value.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => JsonSerializer.Serialize(property.Name) + ":" + Key(property.Value))) + "}",
        JsonValueKind.Array => "A[" + string.Join(",", value.EnumerateArray().Select(Key)) + "]",
        JsonValueKind.String => "S" + JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.Number => NumberKey(value.GetRawText()),
        JsonValueKind.True => "B1",
        JsonValueKind.False => "B0",
        JsonValueKind.Null => "Z",
        _ => throw new JsonException("Undefined JSON value.")
    };

    private static string NumberKey(string raw)
    {
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var exponent = exponentIndex < 0 ? BigInteger.Zero : BigInteger.Parse(raw[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;
        var coefficient = BigInteger.Parse(mantissa.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        if (coefficient.IsZero) return "N0";
        while (coefficient % 10 == 0) { coefficient /= 10; exponent++; }
        return "N" + coefficient.ToString(CultureInfo.InvariantCulture) + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }
}
