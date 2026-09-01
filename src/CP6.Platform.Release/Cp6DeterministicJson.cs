using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Platform.Release;

public static class Cp6DeterministicJson
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const int MaximumDepth = 32;
    public const int MaximumMembers = 256;
    public const int MaximumArrayItems = 4096;
    public const int MaximumStringUtf8Bytes = 65_536;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumBytes)
        {
            throw Error("object-size", $"JSON exceeds {MaximumBytes} bytes.");
        }

        if (utf8Json.Length >= 3 && utf8Json[0] == 0xef && utf8Json[1] == 0xbb && utf8Json[2] == 0xbf)
        {
            throw Error("utf8-bom", "UTF-8 BOM is not allowed.");
        }

        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(utf8Json);
        }
        catch (DecoderFallbackException exception)
        {
            throw Error("invalid-utf8", "Input is not strict UTF-8.", exception);
        }

        ValidateEscapedUnicodeScalars(decoded);
        ValidateTokens(utf8Json);

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Error("root-object", "The JSON root must be an object.");
            }

            var writer = new ArrayBufferWriter<byte>(utf8Json.Length);
            WriteElement(writer, document.RootElement);
            return writer.WrittenSpan.ToArray();
        }
        catch (Cp6ReleaseContractException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Error("invalid-json", "Input is not valid JSON.", exception);
        }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void ValidateTokens(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth + 1
        });
        var containers = new Stack<ContainerState>();
        var firstToken = true;

        try
        {
            while (reader.Read())
            {
                if (firstToken)
                {
                    firstToken = false;
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw Error("root-object", "The JSON root must be an object.");
                    }
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        CountArrayItem(containers);
                        if (reader.CurrentDepth >= MaximumDepth)
                        {
                            throw Error("depth-limit", $"JSON nesting exceeds {MaximumDepth}.");
                        }

                        containers.Push(new ContainerState(isObject: true));
                        break;

                    case JsonTokenType.StartArray:
                        CountArrayItem(containers);
                        if (reader.CurrentDepth >= MaximumDepth)
                        {
                            throw Error("depth-limit", $"JSON nesting exceeds {MaximumDepth}.");
                        }

                        containers.Push(new ContainerState(isObject: false));
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (containers.Count != 0)
                        {
                            containers.Pop();
                        }

                        break;

                    case JsonTokenType.PropertyName:
                        ValidateProperty(reader, containers);
                        break;

                    case JsonTokenType.String:
                        CountArrayItem(containers);
                        ValidateString(ReadString(ref reader));
                        break;

                    case JsonTokenType.Number:
                        CountArrayItem(containers);
                        ValidateNumber(reader.ValueSpan);
                        break;

                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        CountArrayItem(containers);
                        break;
                }
            }
        }
        catch (Cp6ReleaseContractException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Error("invalid-json", "Input is not valid JSON.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw Error("unicode-scalar", "A JSON string contains an invalid Unicode scalar.", exception);
        }

        if (firstToken)
        {
            throw Error("root-object", "The JSON root must be an object.");
        }
    }

    private static void ValidateProperty(Utf8JsonReader reader, Stack<ContainerState> containers)
    {
        if (containers.Count == 0 || !containers.Peek().IsObject)
        {
            throw Error("invalid-json", "A property appeared outside an object.");
        }

        var container = containers.Peek();
        container.Count++;
        if (container.Count > MaximumMembers)
        {
            throw Error("member-limit", $"An object exceeds {MaximumMembers} members.");
        }

        var name = ReadString(ref reader);
        ValidateString(name);
        if (!container.PropertyNames!.Add(name))
        {
            throw Error("duplicate-property", $"Duplicate property '{name}' is not allowed.");
        }
    }

    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.GetString() ?? throw Error("unicode-scalar", "A JSON string contains an invalid Unicode scalar.");

    private static void ValidateString(string value)
    {
        if (!value.IsNormalized(NormalizationForm.FormC))
        {
            throw Error("unicode-normalization", "JSON names and values must be NFC-normalized.");
        }

        if (StrictUtf8.GetByteCount(value) > MaximumStringUtf8Bytes)
        {
            throw Error("string-limit", $"A JSON string exceeds {MaximumStringUtf8Bytes} UTF-8 bytes.");
        }
    }

    private static void ValidateNumber(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0 || raw[0] == (byte)'-' || raw.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0)
        {
            throw Error("number-format", "Only non-negative base-10 integers are allowed.");
        }

        if (!long.TryParse(Encoding.ASCII.GetString(raw), NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw Error("integer-range", "Integer exceeds Int64.MaxValue.");
        }
    }

    private static void CountArrayItem(Stack<ContainerState> containers)
    {
        if (containers.Count == 0 || containers.Peek().IsObject)
        {
            return;
        }

        var container = containers.Peek();
        container.Count++;
        if (container.Count > MaximumArrayItems)
        {
            throw Error("array-limit", $"An array exceeds {MaximumArrayItems} entries.");
        }
    }

    private static void ValidateEscapedUnicodeScalars(string json)
    {
        var inString = false;
        for (var index = 0; index < json.Length; index++)
        {
            var character = json[index];
            if (!inString)
            {
                if (character == '"') inString = true;
                continue;
            }

            if (character == '"')
            {
                inString = false;
                continue;
            }

            if (character != '\\' || index + 1 >= json.Length)
            {
                continue;
            }

            if (json[index + 1] != 'u')
            {
                index++;
                continue;
            }

            if (!TryReadHex16(json, index + 2, out var scalar))
            {
                continue;
            }

            if (char.IsLowSurrogate((char)scalar))
            {
                throw Error("unicode-scalar", "An escaped low surrogate has no high surrogate.");
            }

            if (char.IsHighSurrogate((char)scalar))
            {
                if (index + 11 >= json.Length || json[index + 6] != '\\' || json[index + 7] != 'u' ||
                    !TryReadHex16(json, index + 8, out var low) || !char.IsLowSurrogate((char)low))
                {
                    throw Error("unicode-scalar", "An escaped high surrogate has no low surrogate.");
                }

                index += 11;
                continue;
            }

            index += 5;
        }
    }

    private static bool TryReadHex16(string value, int start, out int scalar)
    {
        scalar = 0;
        if (start + 4 > value.Length) return false;
        for (var offset = 0; offset < 4; offset++)
        {
            var digit = value[start + offset] switch
            {
                >= '0' and <= '9' => value[start + offset] - '0',
                >= 'a' and <= 'f' => value[start + offset] - 'a' + 10,
                >= 'A' and <= 'F' => value[start + offset] - 'A' + 10,
                _ => -1
            };
            if (digit < 0) return false;
            scalar = (scalar << 4) | digit;
        }

        return true;
    }

    private static void WriteElement(ArrayBufferWriter<byte> writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                AppendByte(writer, (byte)'{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!firstProperty) AppendByte(writer, (byte)',');
                    firstProperty = false;
                    WriteString(writer, property.Name);
                    AppendByte(writer, (byte)':');
                    WriteElement(writer, property.Value);
                }

                AppendByte(writer, (byte)'}');
                break;

            case JsonValueKind.Array:
                AppendByte(writer, (byte)'[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) AppendByte(writer, (byte)',');
                    firstItem = false;
                    WriteElement(writer, item);
                }

                AppendByte(writer, (byte)']');
                break;

            case JsonValueKind.String:
                WriteString(writer, element.GetString()!);
                break;

            case JsonValueKind.Number:
                AppendAscii(writer, element.GetInt64().ToString(CultureInfo.InvariantCulture));
                break;

            case JsonValueKind.True:
                AppendAscii(writer, "true");
                break;

            case JsonValueKind.False:
                AppendAscii(writer, "false");
                break;

            case JsonValueKind.Null:
                AppendAscii(writer, "null");
                break;
        }
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        AppendByte(writer, (byte)'"');
        foreach (var rune in value.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"':
                    AppendAscii(writer, "\\\"");
                    break;
                case '\\':
                    AppendAscii(writer, "\\\\");
                    break;
                case <= 0x1f:
                    AppendAscii(writer, "\\u" + rune.Value.ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    AppendUtf8(writer, rune.ToString());
                    break;
            }
        }

        AppendByte(writer, (byte)'"');
    }

    private static void AppendAscii(ArrayBufferWriter<byte> writer, string value) =>
        AppendBytes(writer, Encoding.ASCII.GetBytes(value));

    private static void AppendUtf8(ArrayBufferWriter<byte> writer, string value) =>
        AppendBytes(writer, Encoding.UTF8.GetBytes(value));

    private static void AppendByte(ArrayBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    private static void AppendBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        value.CopyTo(writer.GetSpan(value.Length));
        writer.Advance(value.Length);
    }

    private static Cp6ReleaseContractException Error(string code, string message, Exception? exception = null) =>
        new(code, message, exception);

    private sealed class ContainerState
    {
        public ContainerState(bool isObject)
        {
            IsObject = isObject;
            if (isObject) PropertyNames = new HashSet<string>(StringComparer.Ordinal);
        }

        public bool IsObject { get; }
        public int Count { get; set; }
        public HashSet<string>? PropertyNames { get; }
    }
}
