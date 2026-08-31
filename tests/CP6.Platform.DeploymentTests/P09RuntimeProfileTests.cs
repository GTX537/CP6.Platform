using System.Text;
using System.Text.Json.Nodes;
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09RuntimeProfileTests
{
    private const string ValidProfileJson = """
        {
          "schemaVersion": "1",
          "environmentClass": "NonProduction",
          "profileId": "cp6-platform-p09-ci-v1",
          "runtime": {
            "daprImage": "daprio/daprd:1.18.2",
            "kafkaImage": "apache/kafka:4.3.1",
            "kubectlImage": "registry.k8s.io/kubectl:v1.34.1"
          },
          "identities": {
            "publisherAppId": "cp6-p09-probe-publisher",
            "receiverAppId": "cp6-p09-probe-receiver",
            "provisionerPrincipal": "cp6-p09-provisioner",
            "unauthorizedAppId": "cp6-p09-unauthorized-probe",
            "consumerGroup": "cp6-p09-probe-receiver-v1"
          },
          "components": [
            {
              "name": "cp6-p09-kafka-publish",
              "direction": "Publish",
              "scope": ["cp6-p09-probe-publisher"],
              "usernameSecretRef": "publisher-username",
              "passwordSecretRef": "publisher-password"
            },
            {
              "name": "cp6-p09-kafka-subscribe",
              "direction": "Subscribe",
              "scope": ["cp6-p09-probe-receiver"],
              "usernameSecretRef": "receiver-username",
              "passwordSecretRef": "receiver-password"
            }
          ],
          "topic": {
            "name": "cp6.platform.deployment-probe.v1",
            "eventType": "com.gtx537.platform.contract-example.changed.v1",
            "partitions": 3,
            "retentionMs": 3600000,
            "maxMessageBytes": 1048576
          },
          "acls": [
            {"principal":"cp6-p09-probe-publisher","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Write"},
            {"principal":"cp6-p09-probe-publisher","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Describe"},
            {"principal":"cp6-p09-probe-receiver","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Read"},
            {"principal":"cp6-p09-probe-receiver","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Describe"},
            {"principal":"cp6-p09-probe-receiver","resourceType":"Group","resourceName":"cp6-p09-probe-receiver-v1","operation":"Read"},
            {"principal":"cp6-p09-provisioner","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Create"},
            {"principal":"cp6-p09-provisioner","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Alter"},
            {"principal":"cp6-p09-provisioner","resourceType":"Topic","resourceName":"cp6.platform.deployment-probe.v1","operation":"Describe"},
            {"principal":"cp6-p09-provisioner","resourceType":"Cluster","resourceName":"kafka-cluster","operation":"Describe"}
          ],
          "compose": {
            "appNetwork": "app",
            "runtimeNetwork": "runtime",
            "bootstrapServers": "kafka:9092",
            "kafkaHostPort": false,
            "hostBinding": "127.0.0.1:0",
            "hostNetwork": false,
            "privileged": false,
            "dockerSocket": false,
            "hostPath": false
          },
          "kubernetes": {
            "namespace": "cp6-p09-ci",
            "nonDeployableLabel": "cp6.io/nondeployable=true",
            "defaultDeny": true,
            "dnsEgress": true,
            "minimalProbeIngress": true,
            "minimalKafkaEgress": true,
            "forbiddenKinds": ["Secret","ClusterRole","ClusterRoleBinding","Ingress","PersistentVolume"]
          },
          "evidence": {
            "schemaId": "https://cp6.example/contracts/p09/rehearsal-evidence.v1.schema.json",
            "requiredChecks": ["profile-valid","provision-first","provision-idempotent","invoke-positive","pubsub-positive","direct-kafka-denied","principal-denied","appid-scope-denied","foreign-topic-denied","kubernetes-render","kubernetes-policy","zero-residue"]
          }
        }
        """;

    public static TheoryData<string, string> RequiredRejectionCases => new()
    {
        { "duplicate-root", "duplicate-property" },
        { "unknown-root", "unknown-property" },
        { "production-environment", "production-environment" },
        { "crm-app-id", "crm-app-id" },
        { "crm-topic", "crm-topic" },
        { "floating-dapr-image", "floating-dapr-image" },
        { "floating-kafka-image", "floating-kafka-image" },
        { "external-host", "external-host" },
        { "fixed-public-port", "fixed-public-port" },
        { "wrong-partitions", "wrong-partitions" },
        { "write-on-receiver", "write-on-receiver" }
    };

    public static TheoryData<string> InvalidJsonCases => new()
    {
        "{",
        "{} trailing",
        "{/* comment */}",
        "{\"schemaVersion\":1,}",
        $"{new string('[', 65)}0{new string(']', 65)}"
    };

    public static TheoryData<string> InvalidStringCases => new()
    {
        "empty",
        "non-nfc",
        "carriage-return",
        "nul"
    };

    public static TheoryData<string> AclMutationCases => new()
    {
        "removed",
        "reordered",
        "duplicated"
    };

    [Fact]
    public void Parse_ValidProfile_ExposesCanonicalReadOnlyView()
    {
        var profile = Cp6P09RuntimeProfile.Parse(ValidProfileJson);
        var canonical = profile.ToCanonicalUtf8();
        var fromUtf8 = Cp6P09RuntimeProfile.Parse(canonical);

        Assert.Equal("cp6-platform-p09-ci-v1", Cp6P09RuntimeProfile.ExpectedProfileId);
        Assert.Equal("cp6.platform.deployment-probe.v1", Cp6P09RuntimeProfile.ExpectedTopic);
        Assert.Equal("cp6-p09-probe-receiver-v1", Cp6P09RuntimeProfile.ExpectedConsumerGroup);
        Assert.Equal("NonProduction", profile.EnvironmentClass);
        Assert.Equal(Cp6P09RuntimeProfile.ExpectedProfileId, profile.ProfileId);
        Assert.Equal(Cp6P09RuntimeProfile.ExpectedTopic, profile.TopicName);
        Assert.Equal("com.gtx537.platform.contract-example.changed.v1", profile.EventType);
        Assert.Equal(3, profile.Partitions);
        Assert.Equal("cp6-p09-probe-publisher", profile.PublisherAppId);
        Assert.Equal("cp6-p09-probe-receiver", profile.ReceiverAppId);
        Assert.Equal("cp6-p09-unauthorized-probe", profile.UnauthorizedAppId);
        Assert.Equal("cp6-p09-kafka-publish", profile.PublishComponentName);
        Assert.Equal("cp6-p09-kafka-subscribe", profile.SubscribeComponentName);
        Assert.Matches("^[0-9a-f]{64}$", profile.Sha256);
        Assert.Equal(Cp6P09Json.Canonicalize(ValidProfileJson), Encoding.UTF8.GetString(canonical));
        Assert.Equal(canonical, Cp6P09Json.Canonicalize(canonical));
        Assert.Equal(canonical, fromUtf8.ToCanonicalUtf8());
        Assert.Equal(profile.Sha256, fromUtf8.Sha256);
        Assert.Equal(Cp6P09Json.Sha256Hex(canonical), profile.Sha256);
        Assert.Equal((byte)'{', canonical[0]);
        Assert.DoesNotContain((byte)'\n', canonical);
        Assert.DoesNotContain((byte)'\r', canonical);
        Assert.False(canonical.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void Parse_StringSchemaVersion_AcceptsAndExposesString()
    {
        var profile = Cp6P09RuntimeProfile.Parse(ValidProfileJson);

        string schemaVersion = profile.SchemaVersion;
        Assert.Equal("1", schemaVersion);
    }

    [Fact]
    public void ContractException_ExposesReadOnlyCheckId()
    {
        var exception = new Cp6P09ContractException("profile-valid", "Profile is invalid.");

        Assert.Equal("profile-valid", exception.CheckId);
        Assert.Equal("Profile is invalid.", exception.Message);
        Assert.False(typeof(Cp6P09ContractException).GetProperty(nameof(Cp6P09ContractException.CheckId))!.CanWrite);
    }

    [Fact]
    public void Canonicalize_SortsObjectsRecursivelyAndPreservesArrayOrderAndPrimitives()
    {
        const string json = "{\"z\":2.50,\"a\":{\"y\":true,\"x\":null},\"items\":[3,1,2]}";

        var canonical = Cp6P09Json.Canonicalize(json);

        Assert.Equal("{\"a\":{\"x\":null,\"y\":true},\"items\":[3,1,2],\"z\":2.50}", canonical);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Cp6P09Json.Sha256Hex("abc"u8));
    }

    [Theory]
    [MemberData(nameof(RequiredRejectionCases))]
    public void Parse_RequiredInvalidProfile_ThrowsStableCheckId(string mutation, string expectedCheckId)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(BuildInvalidProfile(mutation)));

        Assert.Equal(expectedCheckId, exception.CheckId);
    }

    [Fact]
    public void Parse_MissingProperty_ThrowsStableCheckId()
    {
        var root = ParseValidRoot();
        root.Remove("evidence");

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("missing-property", exception.CheckId);
    }

    [Fact]
    public void Parse_NumericSchemaVersion_ThrowsWrongType()
    {
        var root = ParseValidRoot();
        root["schemaVersion"] = 1;

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("wrong-type", exception.CheckId);
    }

    [Fact]
    public void Parse_UnknownNestedProperty_ThrowsStableCheckId()
    {
        var root = ParseValidRoot();
        root["runtime"]!.AsObject()["unexpected"] = true;

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("unknown-property", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(InvalidJsonCases))]
    public void Parse_MalformedTrailingCommentOrDeepJson_ThrowsInvalidJson(string json)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Fact]
    public void Parse_InvalidUtf8_ThrowsInvalidJson()
    {
        byte[] invalidUtf8 = [(byte)'{', (byte)'\"', (byte)'x', (byte)'\"', (byte)':', (byte)'\"', 0xFF, (byte)'\"', (byte)'}'];

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(invalidUtf8));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(InvalidStringCases))]
    public void Parse_InvalidStringContent_ThrowsStableCheckId(string mutation)
    {
        var root = ParseValidRoot();
        root["evidence"]!.AsObject()["schemaId"] = mutation switch
        {
            "empty" => string.Empty,
            "non-nfc" => "https://cp6.example/cafe\u0301",
            "carriage-return" => "bad\rvalue",
            "nul" => "bad\0value",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("invalid-string", exception.CheckId);
    }

    [Fact]
    public void Parse_DuplicateNestedProperty_ThrowsBeforeMaterialization()
    {
        var json = ValidProfileJson.Replace(
            "\"daprImage\": \"daprio/daprd:1.18.2\"",
            "\"daprImage\": \"daprio/daprd:1.18.2\", \"daprImage\": \"daprio/daprd:1.18.2\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));

        Assert.Equal("duplicate-property", exception.CheckId);
    }

    [Fact]
    public void Parse_PlaintextSecretFieldName_ThrowsStableCheckId()
    {
        var root = ParseValidRoot();
        root["runtime"]!.AsObject()["PASSWORD"] = "do-not-accept";

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("plaintext-secret", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(AclMutationCases))]
    public void Parse_AclRemovalReorderOrDuplicate_ThrowsStableCheckId(string mutation)
    {
        var root = ParseValidRoot();
        var acls = root["acls"]!.AsArray();
        switch (mutation)
        {
            case "removed":
                acls.RemoveAt(acls.Count - 1);
                break;
            case "reordered":
                var first = acls[0]!.DeepClone();
                acls.RemoveAt(0);
                acls.Insert(1, first);
                break;
            case "duplicated":
                acls.Add(acls[0]!.DeepClone());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("acl-mismatch", exception.CheckId);
    }

    [Fact]
    public void Parse_ComponentScopeWidening_ThrowsStableCheckId()
    {
        var root = ParseValidRoot();
        root["components"]![0]!["scope"]!.AsArray().Add("cp6-p09-unauthorized-probe");

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("component-scope", exception.CheckId);
    }

    [Fact]
    public void ToCanonicalUtf8_ReturnsDefensiveCopies()
    {
        var profile = Cp6P09RuntimeProfile.Parse(ValidProfileJson);
        var first = profile.ToCanonicalUtf8();
        var expected = first.ToArray();

        first[0] = (byte)'[';
        var second = profile.ToCanonicalUtf8();

        Assert.NotSame(first, second);
        Assert.Equal(expected, second);
        Assert.Equal(profile.Sha256, Cp6P09Json.Sha256Hex(second));
    }

    private static JsonObject ParseValidRoot() => JsonNode.Parse(ValidProfileJson)!.AsObject();

    private static string BuildInvalidProfile(string mutation)
    {
        if (mutation == "duplicate-root")
        {
            return "{\"schemaVersion\":1,\"schemaVersion\":1}";
        }

        var root = ParseValidRoot();
        switch (mutation)
        {
            case "unknown-root":
                root["unexpected"] = true;
                break;
            case "production-environment":
                root["environmentClass"] = "Production";
                break;
            case "crm-app-id":
                root["identities"]!["publisherAppId"] = "cp6-crm-publisher";
                break;
            case "crm-topic":
                root["topic"]!["name"] = "cp6.crm.events.v1";
                break;
            case "floating-dapr-image":
                root["runtime"]!["daprImage"] = "daprio/daprd:latest";
                break;
            case "floating-kafka-image":
                root["runtime"]!["kafkaImage"] = "apache/kafka:4";
                break;
            case "external-host":
                root["compose"]!["bootstrapServers"] = "broker.example.com:9092";
                break;
            case "fixed-public-port":
                root["compose"]!["hostBinding"] = "0.0.0.0:9092";
                break;
            case "wrong-partitions":
                root["topic"]!["partitions"] = 6;
                break;
            case "write-on-receiver":
                root["acls"]![3]!["operation"] = "Write";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        return root.ToJsonString();
    }
}
