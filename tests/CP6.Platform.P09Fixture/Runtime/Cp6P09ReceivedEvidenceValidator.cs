using System.Text.Json;

internal static class Cp6P09ReceivedEvidenceValidator
{
    private const int MaximumEvidenceBytes = 16_384;
    private static readonly string[] ExpectedProperties =
    [
        "eventId",
        "eventType",
        "topicName",
        "partitionKey",
        "region",
        "traceId",
        "publisherSpanId",
        "receiverSpanId",
        "receiverParentSpanId",
        "contractValid"
    ];

    internal static bool TryValidate(
        ReadOnlySpan<byte> utf8,
        string expectedEventId,
        string expectedPartitionKey,
        string expectedEventType,
        string expectedTopic,
        out ReceivedEventEvidence? evidence)
    {
        evidence = null;
        if (utf8.IsEmpty || utf8.Length > MaximumEvidenceBytes ||
            !Cp6P09ProbeIdentifier.IsMethodSegment(expectedEventId) ||
            !Cp6P09ProbeIdentifier.IsValid(expectedPartitionKey) ||
            string.IsNullOrEmpty(expectedEventType) ||
            string.IsNullOrEmpty(expectedTopic))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactShape(root))
            {
                return false;
            }

            var eventId = ExactString(root, "eventId");
            var eventType = ExactString(root, "eventType");
            var topicName = ExactString(root, "topicName");
            var partitionKey = ExactString(root, "partitionKey");
            var region = ExactString(root, "region");
            var traceId = ExactString(root, "traceId");
            var publisherSpanId = ExactString(root, "publisherSpanId");
            var receiverSpanId = ExactString(root, "receiverSpanId");
            var receiverParentSpanId = ExactString(root, "receiverParentSpanId");
            if (eventId is null || eventType is null || topicName is null || partitionKey is null ||
                region is null || traceId is null || publisherSpanId is null || receiverSpanId is null ||
                receiverParentSpanId is null ||
                root.GetProperty("contractValid").ValueKind != JsonValueKind.True ||
                !string.Equals(eventId, expectedEventId, StringComparison.Ordinal) ||
                !string.Equals(eventType, expectedEventType, StringComparison.Ordinal) ||
                !string.Equals(topicName, expectedTopic, StringComparison.Ordinal) ||
                !string.Equals(partitionKey, expectedPartitionKey, StringComparison.Ordinal) ||
                !string.Equals(region, "TEST", StringComparison.Ordinal) ||
                !Cp6P09TraceTopology.TryParseObservedContext(
                    traceId,
                    publisherSpanId,
                    isRemote: false,
                    out var publisherTrace) ||
                !Cp6P09TraceTopology.TryParseObservedContext(
                    traceId,
                    receiverSpanId,
                    isRemote: true,
                    out var receiverTrace) ||
                !Cp6P09TraceTopology.TryParseObservedSpanId(
                    receiverParentSpanId,
                    out var parentSpanId) ||
                !Cp6P09TraceTopology.TryCreateDelivery(
                    publisherTrace,
                    receiverTrace,
                    parentSpanId,
                    out _))
            {
                return false;
            }

            evidence = new ReceivedEventEvidence(
                eventId,
                eventType,
                topicName,
                partitionKey,
                region,
                traceId,
                publisherSpanId,
                receiverSpanId,
                receiverParentSpanId,
                true);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasExactShape(JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!ExpectedProperties.Contains(property.Name, StringComparer.Ordinal) ||
                !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.Count == ExpectedProperties.Length;
    }

    private static string? ExactString(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
