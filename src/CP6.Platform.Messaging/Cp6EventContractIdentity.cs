using System.Text.RegularExpressions;

namespace CP6.Platform.Messaging;

/// <summary>
/// Parses the CP6 event type and maps it to its canonical major-version schema identifier.
/// </summary>
public sealed partial record Cp6EventContractIdentity
{
    public const string SchemaBaseUri = "https://contracts.cp6.uk/events/";

    private Cp6EventContractIdentity(string eventType, string producer, string eventName, int majorVersion)
    {
        EventType = eventType;
        Producer = producer;
        EventName = eventName;
        MajorVersion = majorVersion;
        EventSlug = eventName.Replace('.', '-');
        SchemaId = new Uri($"{SchemaBaseUri}{producer}/{EventSlug}/v{majorVersion}/schema.json", UriKind.Absolute);
    }

    public string EventType { get; }

    public string Producer { get; }

    public string EventName { get; }

    public string EventSlug { get; }

    public int MajorVersion { get; }

    public Uri SchemaId { get; }

    public static Cp6EventContractIdentity Parse(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        var match = EventTypePattern().Match(eventType);
        if (!match.Success || !int.TryParse(match.Groups["major"].Value, out var majorVersion))
        {
            throw new ArgumentException("Event type must use com.gtx537.<producer>.<event-name>.v<major>.", nameof(eventType));
        }

        return new Cp6EventContractIdentity(
            eventType,
            match.Groups["producer"].Value,
            match.Groups["name"].Value,
            majorVersion);
    }

    [GeneratedRegex(
        "^com\\.gtx537\\.(?<producer>[a-z][a-z0-9-]{1,31})\\.(?<name>[a-z][a-z0-9-]*(?:\\.[a-z][a-z0-9-]*)*)\\.v(?<major>[1-9][0-9]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();
}
