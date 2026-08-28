using System.Text.RegularExpressions;

namespace CP6.Platform.Messaging;

/// <summary>
/// Defines the single CP6 Dapr/Kafka addressing profile.
/// </summary>
public static partial class Cp6DaprKafkaConventions
{
    public const string PubSubName = "cp6-kafka-pubsub";
    public const string PartitionKeyMetadata = "partitionKey";
    public const int KafkaTopicMaxLength = 249;

    public static string GetTopic(Cp6EventContractIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var topic = $"cp6.{identity.Producer}.{identity.EventSlug}.v{identity.MajorVersion}";
        if (topic.Length > KafkaTopicMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(identity), "The canonical Kafka topic exceeds 249 characters.");
        }

        return topic;
    }

    public static string GetPartitionKey(Guid tenantId, string aggregateId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenant id is required.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        Cp6CloudEventAttributes.AggregateId.Validate(aggregateId);
        return $"{tenantId:D}/{aggregateId}";
    }

    public static void ValidateAppId(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        if (!AppIdPattern().IsMatch(appId))
        {
            throw new ArgumentException("Dapr app id must be a lowercase CP6 DNS label.", nameof(appId));
        }
    }

    public static void ValidateMethodName(string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (methodName.Length > 256 ||
            methodName.StartsWith("/", StringComparison.Ordinal) ||
            methodName.Contains("//", StringComparison.Ordinal) ||
            methodName.Contains('?') ||
            methodName.Contains('#') ||
            methodName.Split('/').Any(segment => segment is "." or ".." || !MethodSegmentPattern().IsMatch(segment)))
        {
            throw new ArgumentException("Dapr method name must be a canonical relative path without traversal, query, or fragment.", nameof(methodName));
        }
    }

    [GeneratedRegex("^cp6-[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MethodSegmentPattern();
}
