using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using CP6.Platform.Abstractions;

namespace CP6.Platform.Messaging;

internal static class Cp6TraceContextCodec
{
    private const string InvalidTraceContext = "invalid_trace_context";
    private static readonly Meter Meter = new(Cp6TelemetryMeters.Messaging);
    private static readonly Counter<long> RejectedCounter = Meter.CreateCounter<long>(
        "cp6.messaging.trace_context.rejected");

    internal static ActivityContext? TryExtract(ReadOnlyMemory<byte> structuredEvent)
    {
        try
        {
            using var document = JsonDocument.Parse(structuredEvent, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement? traceParent = null;
            JsonElement? traceState = null;
            var traceParentCount = 0;
            var traceStateCount = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("traceparent"))
                {
                    traceParent = property.Value;
                    traceParentCount++;
                }
                else if (property.NameEquals("tracestate"))
                {
                    traceState = property.Value;
                    traceStateCount++;
                }
            }

            if (traceParentCount == 0 && traceStateCount == 0)
            {
                return null;
            }

            if (traceParentCount != 1 ||
                traceStateCount > 1 ||
                traceParent?.ValueKind != JsonValueKind.String ||
                (traceStateCount == 1 && traceState?.ValueKind != JsonValueKind.String))
            {
                return Reject();
            }

            var parentValue = traceParent.Value.GetString();
            var stateValue = traceStateCount == 1 ? traceState!.Value.GetString() : null;
            if (string.IsNullOrEmpty(parentValue) ||
                parentValue.Length > 55 ||
                stateValue?.Length > 512 ||
                (traceStateCount == 1 && !IsValidTraceState(stateValue)) ||
                !ActivityContext.TryParse(parentValue, stateValue, isRemote: true, out var context))
            {
                return Reject();
            }

            return context;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool IsValidTraceState(string? traceState)
    {
        if (string.IsNullOrEmpty(traceState))
        {
            return false;
        }

        var members = traceState.Split(',');
        if (members.Length > 32)
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawMember in members)
        {
            var member = rawMember.Trim(' ', '\t');
            var separator = member.IndexOf('=');
            if (separator <= 0 ||
                separator != member.LastIndexOf('=') ||
                !IsValidTraceStateKey(member.AsSpan(0, separator)) ||
                !IsValidTraceStateValue(member.AsSpan(separator + 1)) ||
                !keys.Add(member[..separator]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidTraceStateKey(ReadOnlySpan<char> key)
    {
        if (key.IsEmpty || key.Length > 256)
        {
            return false;
        }

        var at = key.IndexOf('@');
        if (at < 0)
        {
            return IsSimpleKey(key);
        }

        if (at == 0 || at != key.LastIndexOf('@') || at > 241 || key.Length - at - 1 is < 1 or > 14)
        {
            return false;
        }

        return IsTenantKey(key[..at]) &&
            IsLowerAlpha(key[at + 1]) &&
            IsKeyTail(key[(at + 2)..]);
    }

    private static bool IsSimpleKey(ReadOnlySpan<char> key) =>
        IsLowerAlpha(key[0]) && IsKeyTail(key[1..]);

    private static bool IsTenantKey(ReadOnlySpan<char> key) =>
        (IsLowerAlpha(key[0]) || char.IsAsciiDigit(key[0])) && IsKeyTail(key[1..]);

    private static bool IsKeyTail(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!IsLowerAlpha(character) &&
                !char.IsAsciiDigit(character) &&
                character is not '_' and not '-' and not '*' and not '/')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidTraceStateValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value[^1] == ' ')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < (char)0x20 or > (char)0x7e or ',' or '=')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerAlpha(char value) => value is >= 'a' and <= 'z';

    private static ActivityContext? Reject()
    {
        RejectedCounter.Add(
            1,
            new KeyValuePair<string, object?>(
                Cp6TelemetryConventions.ErrorCodeTag,
                InvalidTraceContext));
        return null;
    }
}
