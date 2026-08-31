using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Platform.Deployment;

public static class Cp6P09Json
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    public static string Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return Encoding.UTF8.GetString(Canonicalize(StrictUtf8.GetBytes(json)));
        }
        catch (EncoderFallbackException exception)
        {
            throw InvalidJson(exception);
        }
    }

    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        ValidateSyntaxAndDuplicates(utf8Json);

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), DocumentOptions);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(writer, document.RootElement);
            }

            return stream.ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw InvalidJson(exception);
        }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void ValidateSyntaxAndDuplicates(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(utf8Json);
            var reader = new Utf8JsonReader(utf8Json, isFinalBlock: true, new JsonReaderState(ReaderOptions));
            var objectProperties = new Stack<HashSet<string>>();
            var hasToken = false;

            while (reader.Read())
            {
                hasToken = true;
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.PropertyName:
                        if (objectProperties.Count == 0)
                        {
                            throw new JsonException("A property name occurred outside an object.");
                        }

                        var propertyName = reader.GetString()!;
                        if (!objectProperties.Peek().Add(propertyName))
                        {
                            throw new Cp6P09ContractException(
                                "duplicate-property",
                                "A JSON object contains a duplicate property name.");
                        }

                        break;
                    case JsonTokenType.String:
                        _ = reader.GetString();
                        break;
                    case JsonTokenType.EndObject:
                        if (objectProperties.Count == 0)
                        {
                            throw new JsonException("An object ended without a matching start token.");
                        }

                        objectProperties.Pop();
                        break;
                }
            }

            if (!hasToken || objectProperties.Count != 0)
            {
                throw new JsonException("The JSON payload is empty or incomplete.");
            }
        }
        catch (Cp6P09ContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            throw InvalidJson(exception);
        }
    }

    private static Cp6P09ContractException InvalidJson(Exception exception) =>
        new("invalid-json", "The runtime profile is not valid strict UTF-8 JSON.", exception);

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.Number:
                if (IsNegativeZero(element.GetRawText()))
                {
                    writer.WriteNumberValue(0);
                }
                else
                {
                    element.WriteTo(writer);
                }

                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsNegativeZero(string rawNumber)
    {
        if (!rawNumber.StartsWith("-0", StringComparison.Ordinal))
        {
            return false;
        }

        var exponentIndex = rawNumber.IndexOfAny(['e', 'E']);
        var significandEnd = exponentIndex < 0 ? rawNumber.Length : exponentIndex;
        for (var index = 1; index < significandEnd; index++)
        {
            if (rawNumber[index] is not ('0' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
