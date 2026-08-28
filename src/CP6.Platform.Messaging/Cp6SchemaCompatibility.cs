using System.Text.Json;

namespace CP6.Platform.Messaging;

/// <summary>
/// Enforces the CP6 same-major additive compatibility policy for event schemas.
/// </summary>
public static class Cp6SchemaCompatibility
{
    public static Cp6SchemaCompatibilityResult Compare(string publishedSchemaJson, string candidateSchemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedSchemaJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateSchemaJson);

        using var published = JsonDocument.Parse(publishedSchemaJson);
        using var candidate = JsonDocument.Parse(candidateSchemaJson);
        var issues = new List<Cp6SchemaCompatibilityIssue>();
        CompareObject(published.RootElement, candidate.RootElement, "", issues);
        return new Cp6SchemaCompatibilityResult(issues.Count == 0, issues);
    }

    private static void CompareObject(
        JsonElement published,
        JsonElement candidate,
        string path,
        List<Cp6SchemaCompatibilityIssue> issues)
    {
        if (published.ValueKind != JsonValueKind.Object || candidate.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(path, "SCHEMA_NODE_CHANGED"));
            return;
        }

        CompareExactKeyword(published, candidate, "$schema", path, issues);
        CompareExactKeyword(published, candidate, "$id", path, issues);
        CompareExactKeyword(published, candidate, "type", path, issues);
        CompareExactKeyword(published, candidate, "const", path, issues);
        CompareExactKeyword(published, candidate, "enum", path, issues);
        CompareExactKeyword(published, candidate, "format", path, issues);
        CompareExactKeyword(published, candidate, "pattern", path, issues);
        CompareExactKeyword(published, candidate, "propertyNames", path, issues);
        CompareRelaxableLowerBound(published, candidate, "minLength", path, issues);
        CompareRelaxableLowerBound(published, candidate, "minimum", path, issues);
        CompareRelaxableUpperBound(published, candidate, "maxLength", path, issues);
        CompareRelaxableUpperBound(published, candidate, "maximum", path, issues);

        if (IsObjectSchema(published))
        {
            if (candidate.TryGetProperty("additionalProperties", out var additionalProperties) &&
                additionalProperties.ValueKind != JsonValueKind.True)
            {
                issues.Add(new(path, "UNKNOWN_PROPERTIES_REJECTED"));
            }

            var publishedRequired = ReadStringSet(published, "required");
            var candidateRequired = ReadStringSet(candidate, "required");
            if (!publishedRequired.SetEquals(candidateRequired))
            {
                issues.Add(new(path, "REQUIRED_SET_CHANGED"));
            }

            var publishedProperties = ReadProperties(published);
            var candidateProperties = ReadProperties(candidate);
            foreach (var (propertyName, publishedProperty) in publishedProperties)
            {
                var propertyPath = $"{path}/properties/{propertyName}";
                if (!candidateProperties.TryGetValue(propertyName, out var candidateProperty))
                {
                    issues.Add(new(propertyPath, "PUBLISHED_PROPERTY_REMOVED"));
                    continue;
                }

                CompareObject(publishedProperty, candidateProperty, propertyPath, issues);
            }
        }
    }

    private static void CompareExactKeyword(
        JsonElement published,
        JsonElement candidate,
        string keyword,
        string path,
        List<Cp6SchemaCompatibilityIssue> issues)
    {
        var hasPublished = published.TryGetProperty(keyword, out var publishedValue);
        var hasCandidate = candidate.TryGetProperty(keyword, out var candidateValue);
        if (hasPublished != hasCandidate ||
            (hasPublished && !JsonEquals(publishedValue, candidateValue)))
        {
            issues.Add(new($"{path}/{keyword}", "CONSTRAINT_CHANGED"));
        }
    }

    private static void CompareRelaxableLowerBound(
        JsonElement published,
        JsonElement candidate,
        string keyword,
        string path,
        List<Cp6SchemaCompatibilityIssue> issues)
    {
        var hasPublished = published.TryGetProperty(keyword, out var publishedValue);
        var hasCandidate = candidate.TryGetProperty(keyword, out var candidateValue);
        if ((!hasPublished && hasCandidate) ||
            (hasPublished && hasCandidate && candidateValue.GetDecimal() > publishedValue.GetDecimal()))
        {
            issues.Add(new($"{path}/{keyword}", "CONSTRAINT_TIGHTENED"));
        }
    }

    private static void CompareRelaxableUpperBound(
        JsonElement published,
        JsonElement candidate,
        string keyword,
        string path,
        List<Cp6SchemaCompatibilityIssue> issues)
    {
        var hasPublished = published.TryGetProperty(keyword, out var publishedValue);
        var hasCandidate = candidate.TryGetProperty(keyword, out var candidateValue);
        if ((!hasPublished && hasCandidate) ||
            (hasPublished && hasCandidate && candidateValue.GetDecimal() < publishedValue.GetDecimal()))
        {
            issues.Add(new($"{path}/{keyword}", "CONSTRAINT_TIGHTENED"));
        }
    }

    private static bool IsObjectSchema(JsonElement schema) =>
        schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object";

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectEquals(left, right),
            JsonValueKind.Array => left.EnumerateArray().SequenceEqual(right.EnumerateArray(), JsonElementComparer.Instance),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static bool ObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        var rightProperties = right.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        return leftProperties.Count == rightProperties.Count &&
            leftProperties.All(pair => rightProperties.TryGetValue(pair.Key, out var rightValue) && JsonEquals(pair.Value, rightValue));
    }

    private sealed class JsonElementComparer : IEqualityComparer<JsonElement>
    {
        public static JsonElementComparer Instance { get; } = new();

        public bool Equals(JsonElement left, JsonElement right) => JsonEquals(left, right);

        public int GetHashCode(JsonElement value) => value.GetRawText().GetHashCode(StringComparison.Ordinal);
    }

    private static HashSet<string> ReadStringSet(JsonElement schema, string propertyName)
    {
        if (!schema.TryGetProperty(propertyName, out var values))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return values.EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadProperties(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties))
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        return properties.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value,
            StringComparer.Ordinal);
    }
}

public sealed record Cp6SchemaCompatibilityResult(
    bool IsBackwardCompatible,
    IReadOnlyList<Cp6SchemaCompatibilityIssue> Issues);

public sealed record Cp6SchemaCompatibilityIssue(string SchemaLocation, string Code);
