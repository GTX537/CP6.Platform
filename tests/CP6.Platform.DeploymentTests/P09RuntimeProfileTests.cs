using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09RuntimeProfileTests
{
    private static readonly string ValidProfileJson =
        P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json");

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

    public static TheoryData<string, string> FixedInvariantRejectionCases => new()
    {
        { "schema-version", "schema-version" },
        { "profile-id", "profile-id" },
        { "orchestration-image", "kubectl-image" },
        { "receiver-app-id", "crm-app-id" },
        { "provisioner-principal", "crm-app-id" },
        { "unauthorized-app-id", "crm-app-id" },
        { "consumer-group", "consumer-group" },
        { "publish-name", "component-mismatch" },
        { "publish-direction", "component-mismatch" },
        { "publish-scope", "component-scope" },
        { "publish-username-ref", "component-mismatch" },
        { "publish-password-ref", "component-mismatch" },
        { "subscribe-name", "component-mismatch" },
        { "subscribe-direction", "component-mismatch" },
        { "subscribe-scope", "component-scope" },
        { "subscribe-username-ref", "component-mismatch" },
        { "subscribe-password-ref", "component-mismatch" },
        { "component-count", "component-mismatch" },
        { "event-type", "event-type" },
        { "retention", "topic-retention" },
        { "max-message-bytes", "topic-message-size" },
        { "app-network", "compose-network" },
        { "runtime-network", "compose-network" },
        { "kafka-host-port", "kafka-host-port" },
        { "host-network", "host-network" },
        { "privileged", "privileged-runtime" },
        { "docker-socket", "container-socket" },
        { "host-path", "host-path" },
        { "kubernetes-namespace", "cluster-namespace" },
        { "kubernetes-label", "nondeployable-label" },
        { "default-deny", "cluster-policy" },
        { "dns-egress", "cluster-policy" },
        { "probe-ingress", "cluster-policy" },
        { "kafka-egress", "cluster-policy" },
        { "forbidden-kinds-order", "forbidden-kinds" },
        { "forbidden-kinds-content", "forbidden-kinds" },
        { "evidence-schema", "evidence-schema" },
        { "required-check-removed", "required-checks" },
        { "required-check-reordered", "required-checks" },
        { "required-check-duplicated", "required-checks" },
        { "nested-missing", "missing-property" },
        { "nested-unknown", "unknown-property" },
        { "nested-wrong-type", "wrong-type" }
    };

    public static TheoryData<string> EscapedLoneSurrogateJson => new()
    {
        "{\"x\":\"\\uD800\"}",
        "{\"\\uD800\":\"x\"}"
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

    public static TheoryData<string, string> IntegralNumberAliases => new()
    {
        { "0.0", "0" },
        { "0e0", "0" },
        { "-0.0", "0" },
        { "3.0", "3" },
        { "3600000e0", "3600000" },
        { "1.5e1", "15" },
        { "-3.00e0", "-3" }
    };

    public static TheoryData<string, string> ProfileIntegralNumberAliases => new()
    {
        { "\"partitions\": 3", "\"partitions\": 3.0" },
        { "\"retentionMs\": 3600000", "\"retentionMs\": 3600000e0" },
        { "\"maxMessageBytes\": 1048576", "\"maxMessageBytes\": 1048576.0" }
    };

    public static IEnumerable<object[]> AdversarialUnknownPropertyNames =>
        P09ContractTestData.AdversarialUnknownPropertyNames.Select(value => new object[] { value });

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
    public void CanonicalProfileSha256_IsFrozenAsPublicV1Constant()
    {
        const string expected = "94addf0349ff895f21eca3e0d660c8d5159198267080df9109ff6493c1063681";
        var field = typeof(Cp6P09RuntimeProfile).GetField("ExpectedSha256");
        var profile = Cp6P09RuntimeProfile.Parse(ValidProfileJson);

        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
        Assert.Equal(expected, field.GetRawConstantValue());
        Assert.Equal(expected, profile.Sha256);
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
    [InlineData("-0")]
    [InlineData("-0.0")]
    [InlineData("-0e0")]
    [InlineData("-0E+10")]
    public void Canonicalize_NormalizesEveryValidNegativeZeroLexemeAndHash(string negativeZero)
    {
        var zero = Cp6P09Json.Canonicalize("{\"value\":0}");
        var normalized = Cp6P09Json.Canonicalize($"{{\"value\":{negativeZero}}}");

        Assert.Equal(zero, normalized);
        Assert.Equal(
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(zero)),
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(normalized)));
    }

    [Theory]
    [MemberData(nameof(IntegralNumberAliases))]
    public void Canonicalize_NormalizesMathematicallyIntegralAliasesWithoutChangingIdentity(
        string alias,
        string expectedInteger)
    {
        var canonicalInteger = $"{{\"value\":{expectedInteger}}}";
        var normalized = Cp6P09Json.Canonicalize($"{{\"value\":{alias}}}");

        Assert.Equal(canonicalInteger, normalized);
        Assert.Equal(
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(canonicalInteger)),
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(normalized)));
    }

    [Theory]
    [MemberData(nameof(ProfileIntegralNumberAliases))]
    public void Parse_MathematicallyIntegralProfileAliases_AcceptsAndEmitsFrozenCanonicalIntegers(
        string canonicalLexeme,
        string aliasLexeme)
    {
        var aliasProfile = ValidProfileJson.Replace(canonicalLexeme, aliasLexeme, StringComparison.Ordinal);

        Assert.NotEqual(ValidProfileJson, aliasProfile);
        var profile = Cp6P09RuntimeProfile.Parse(aliasProfile);

        Assert.Equal(Cp6P09RuntimeProfile.ExpectedSha256, profile.Sha256);
        Assert.Equal(Cp6P09Json.Canonicalize(ValidProfileJson), Encoding.UTF8.GetString(profile.ToCanonicalUtf8()));
    }

    [Fact]
    public void Canonicalize_InvalidLeadingZeroNumber_RemainsInvalidJson()
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09Json.Canonicalize("{\"value\":-00}"));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(RequiredRejectionCases))]
    public void Parse_RequiredInvalidProfile_ThrowsStableCheckId(string mutation, string expectedCheckId)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(BuildInvalidProfile(mutation)));

        Assert.Equal(expectedCheckId, exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(FixedInvariantRejectionCases))]
    public void Parse_FixedInvariantMutation_ThrowsStableCheckId(string mutation, string expectedCheckId)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(
            () => Cp6P09RuntimeProfile.Parse(BuildFixedInvariantMutation(mutation)));

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
    [MemberData(nameof(AdversarialUnknownPropertyNames))]
    public void Parse_UnknownProperty_DoesNotEchoAttackerControlledName(string propertyName)
    {
        var root = ParseValidRoot();
        root[propertyName] = true;

        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RuntimeProfile.Parse(root.ToJsonString()));

        Assert.Equal("unknown-property", exception.CheckId);
        Assert.DoesNotContain(propertyName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clientSecret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', exception.Message);
        Assert.DoesNotContain('\n', exception.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidJsonCases))]
    public void Parse_MalformedTrailingCommentOrDeepJson_ThrowsInvalidJson(string json)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Fact]
    public void Parse_InvalidJson_UsesNeutralContractMessage()
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse("{"));

        Assert.Equal("invalid-json", exception.CheckId);
        Assert.Equal("The P09 contract JSON is not valid strict UTF-8 JSON.", exception.Message);
        Assert.DoesNotContain("profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidUtf8_ThrowsInvalidJson()
    {
        byte[] invalidUtf8 = [(byte)'{', (byte)'\"', (byte)'x', (byte)'\"', (byte)':', (byte)'\"', 0xFF, (byte)'\"', (byte)'}'];

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(invalidUtf8));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(EscapedLoneSurrogateJson))]
    public void Parse_EscapedLoneSurrogate_ThrowsInvalidJson(string json)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(EscapedLoneSurrogateJson))]
    public void Canonicalize_EscapedLoneSurrogate_ThrowsInvalidJson(string json)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09Json.Canonicalize(json));

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

    [Theory]
    [InlineData("password=obvious-fake-secret")]
    [InlineData("line\r\nsecret-value")]
    public void Canonicalize_DuplicateProperty_DoesNotEchoAttackerControlledName(string propertyName)
    {
        var encodedName = JsonSerializer.Serialize(propertyName);
        var json = $"{{{encodedName}:1,{encodedName}:2}}";

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09Json.Canonicalize(json));

        Assert.Equal("duplicate-property", exception.CheckId);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', exception.Message);
        Assert.DoesNotContain('\n', exception.Message);
        Assert.DoesNotContain(propertyName, exception.Message, StringComparison.Ordinal);
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

    private static string BuildFixedInvariantMutation(string mutation)
    {
        var root = ParseValidRoot();
        var runtime = root["runtime"]!.AsObject();
        var identities = root["identities"]!.AsObject();
        var components = root["components"]!.AsArray();
        var topic = root["topic"]!.AsObject();
        var compose = root["compose"]!.AsObject();
        var cluster = root["kubernetes"]!.AsObject();
        var evidence = root["evidence"]!.AsObject();

        switch (mutation)
        {
            case "schema-version": root["schemaVersion"] = "2"; break;
            case "profile-id": root["profileId"] = "cp6-platform-p09-other"; break;
            case "orchestration-image": runtime["kubectlImage"] = "registry.k8s.io/kubectl:latest"; break;
            case "receiver-app-id": identities["receiverAppId"] = "cp6-p09-other-receiver"; break;
            case "provisioner-principal": identities["provisionerPrincipal"] = "cp6-p09-other-provisioner"; break;
            case "unauthorized-app-id": identities["unauthorizedAppId"] = "cp6-p09-other-unauthorized"; break;
            case "consumer-group": identities["consumerGroup"] = "cp6-p09-other-group"; break;
            case "publish-name": components[0]!["name"] = "cp6-p09-other-publish"; break;
            case "publish-direction": components[0]!["direction"] = "Subscribe"; break;
            case "publish-scope": components[0]!["scope"]![0] = "cp6-p09-probe-receiver"; break;
            case "publish-username-ref": components[0]!["usernameSecretRef"] = "other-username"; break;
            case "publish-password-ref": components[0]!["passwordSecretRef"] = "other-password"; break;
            case "subscribe-name": components[1]!["name"] = "cp6-p09-other-subscribe"; break;
            case "subscribe-direction": components[1]!["direction"] = "Publish"; break;
            case "subscribe-scope": components[1]!["scope"]![0] = "cp6-p09-probe-publisher"; break;
            case "subscribe-username-ref": components[1]!["usernameSecretRef"] = "other-username"; break;
            case "subscribe-password-ref": components[1]!["passwordSecretRef"] = "other-password"; break;
            case "component-count": components.RemoveAt(components.Count - 1); break;
            case "event-type": topic["eventType"] = "com.gtx537.platform.other.v1"; break;
            case "retention": topic["retentionMs"] = 7_200_000; break;
            case "max-message-bytes": topic["maxMessageBytes"] = 2_097_152; break;
            case "app-network": compose["appNetwork"] = "other-app"; break;
            case "runtime-network": compose["runtimeNetwork"] = "other-runtime"; break;
            case "kafka-host-port": compose["kafkaHostPort"] = true; break;
            case "host-network": compose["hostNetwork"] = true; break;
            case "privileged": compose["privileged"] = true; break;
            case "docker-socket": compose["dockerSocket"] = true; break;
            case "host-path": compose["hostPath"] = true; break;
            case "kubernetes-namespace": cluster["namespace"] = "other"; break;
            case "kubernetes-label": cluster["nonDeployableLabel"] = "cp6.io/nondeployable=false"; break;
            case "default-deny": cluster["defaultDeny"] = false; break;
            case "dns-egress": cluster["dnsEgress"] = false; break;
            case "probe-ingress": cluster["minimalProbeIngress"] = false; break;
            case "kafka-egress": cluster["minimalKafkaEgress"] = false; break;
            case "forbidden-kinds-order": SwapFirstTwo(cluster["forbiddenKinds"]!.AsArray()); break;
            case "forbidden-kinds-content": cluster["forbiddenKinds"]![0] = "ConfigMap"; break;
            case "evidence-schema": evidence["schemaId"] = "https://cp6.example/contracts/other.json"; break;
            case "required-check-removed": evidence["requiredChecks"]!.AsArray().RemoveAt(0); break;
            case "required-check-reordered": SwapFirstTwo(evidence["requiredChecks"]!.AsArray()); break;
            case "required-check-duplicated": evidence["requiredChecks"]!.AsArray().Add("profile-valid"); break;
            case "nested-missing": runtime.Remove("daprImage"); break;
            case "nested-unknown": runtime["unexpected"] = true; break;
            case "nested-wrong-type": runtime["daprImage"] = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        return root.ToJsonString();
    }

    private static void SwapFirstTwo(JsonArray values)
    {
        var first = values[0]!.DeepClone();
        var second = values[1]!.DeepClone();
        values[0] = second;
        values[1] = first;
    }

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
