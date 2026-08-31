using System.Globalization;
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
        new("invalid-json", "The P09 contract JSON is not valid strict UTF-8 JSON.", exception);

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
                if (TryCanonicalizeIntegralNumber(element.GetRawText(), out var canonicalInteger))
                {
                    writer.WriteRawValue(canonicalInteger, skipInputValidation: true);
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

    private static bool TryCanonicalizeIntegralNumber(string rawNumber, out string canonicalInteger)
    {
        const int maxCanonicalDigits = 128;
        canonicalInteger = string.Empty;
        var negative = rawNumber[0] == '-';
        var numberStart = negative ? 1 : 0;
        var exponentIndex = rawNumber.IndexOfAny(['e', 'E']);
        var significandEnd = exponentIndex < 0 ? rawNumber.Length : exponentIndex;
        var significand = rawNumber.AsSpan(numberStart, significandEnd - numberStart);
        var decimalIndex = significand.IndexOf('.');
        var fractionalDigits = decimalIndex < 0 ? 0 : significand.Length - decimalIndex - 1;
        var digits = decimalIndex < 0
            ? significand.ToString()
            : string.Concat(significand[..decimalIndex], significand[(decimalIndex + 1)..]);

        var firstNonzero = 0;
        while (firstNonzero < digits.Length && digits[firstNonzero] == '0')
        {
            firstNonzero++;
        }

        if (firstNonzero == digits.Length)
        {
            canonicalInteger = "0";
            return true;
        }

        var exponent = 0;
        if (exponentIndex >= 0 &&
            !int.TryParse(
                rawNumber.AsSpan(exponentIndex + 1),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out exponent))
        {
            return false;
        }

        var decimalPower = (long)exponent - fractionalDigits;
        var digitEnd = digits.Length;
        var appendedZeros = 0;
        if (decimalPower < 0)
        {
            var requiredTrailingZeros = -decimalPower;
            var availableTrailingZeros = 0;
            for (var index = digits.Length - 1; index >= firstNonzero && digits[index] == '0'; index--)
            {
                availableTrailingZeros++;
            }

            if (requiredTrailingZeros > availableTrailingZeros)
            {
                return false;
            }

            digitEnd -= (int)requiredTrailingZeros;
        }
        else
        {
            if (decimalPower > maxCanonicalDigits)
            {
                return false;
            }

            appendedZeros = (int)decimalPower;
        }

        var magnitudeLength = digitEnd - firstNonzero + appendedZeros;
        if (magnitudeLength is <= 0 or > maxCanonicalDigits)
        {
            return false;
        }

        var magnitude = string.Concat(
            digits.AsSpan(firstNonzero, digitEnd - firstNonzero),
            new string('0', appendedZeros));
        canonicalInteger = negative ? $"-{magnitude}" : magnitude;
        return true;
    }
}
