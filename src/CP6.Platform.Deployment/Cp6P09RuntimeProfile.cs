using System.Text;
using System.Text.Json;

namespace CP6.Platform.Deployment;

public sealed class Cp6P09RuntimeProfile
{
    public const string ExpectedProfileId = "cp6-platform-p09-ci-v1";
    public const string ExpectedTopic = "cp6.platform.deployment-probe.v1";
    public const string ExpectedConsumerGroup = "cp6-p09-probe-receiver-v1";

    private readonly byte[] canonicalUtf8;

    private Cp6P09RuntimeProfile(
        byte[] canonicalUtf8,
        string schemaVersion,
        string environmentClass,
        string profileId,
        string topicName,
        string eventType,
        int partitions,
        string publisherAppId,
        string receiverAppId,
        string unauthorizedAppId,
        string publishComponentName,
        string subscribeComponentName)
    {
        this.canonicalUtf8 = canonicalUtf8.ToArray();
        SchemaVersion = schemaVersion;
        EnvironmentClass = environmentClass;
        ProfileId = profileId;
        TopicName = topicName;
        EventType = eventType;
        Partitions = partitions;
        PublisherAppId = publisherAppId;
        ReceiverAppId = receiverAppId;
        UnauthorizedAppId = unauthorizedAppId;
        PublishComponentName = publishComponentName;
        SubscribeComponentName = subscribeComponentName;
        Sha256 = Cp6P09Json.Sha256Hex(canonicalUtf8);
    }

    public string SchemaVersion { get; }

    public string EnvironmentClass { get; }

    public string ProfileId { get; }

    public string TopicName { get; }

    public string EventType { get; }

    public int Partitions { get; }

    public string PublisherAppId { get; }

    public string ReceiverAppId { get; }

    public string UnauthorizedAppId { get; }

    public string PublishComponentName { get; }

    public string SubscribeComponentName { get; }

    public string Sha256 { get; }

    public byte[] ToCanonicalUtf8() => canonicalUtf8.ToArray();

    public static Cp6P09RuntimeProfile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var canonicalUtf8 = Encoding.UTF8.GetBytes(Cp6P09Json.Canonicalize(json));
        return Create(canonicalUtf8);
    }

    public static Cp6P09RuntimeProfile Parse(ReadOnlySpan<byte> utf8Json)
    {
        var canonicalUtf8 = Cp6P09Json.Canonicalize(utf8Json);
        return Create(canonicalUtf8);
    }

    private static Cp6P09RuntimeProfile Create(byte[] canonicalUtf8)
    {
        using var document = JsonDocument.Parse(canonicalUtf8);
        var root = document.RootElement;
        Cp6P09RuntimeProfileValidator.Validate(root);

        var identities = root.GetProperty("identities");
        var components = root.GetProperty("components");
        var topic = root.GetProperty("topic");

        return new Cp6P09RuntimeProfile(
            canonicalUtf8,
            root.GetProperty("schemaVersion").GetString()!,
            root.GetProperty("environmentClass").GetString()!,
            root.GetProperty("profileId").GetString()!,
            topic.GetProperty("name").GetString()!,
            topic.GetProperty("eventType").GetString()!,
            topic.GetProperty("partitions").GetInt32(),
            identities.GetProperty("publisherAppId").GetString()!,
            identities.GetProperty("receiverAppId").GetString()!,
            identities.GetProperty("unauthorizedAppId").GetString()!,
            components[0].GetProperty("name").GetString()!,
            components[1].GetProperty("name").GetString()!);
    }
}
