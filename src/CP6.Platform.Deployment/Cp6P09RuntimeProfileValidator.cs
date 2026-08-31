using System.Text;
using System.Text.Json;

namespace CP6.Platform.Deployment;

internal static class Cp6P09RuntimeProfileValidator
{
    private const string PublisherAppId = "cp6-p09-probe-publisher";
    private const string ReceiverAppId = "cp6-p09-probe-receiver";
    private const string ProvisionerPrincipal = "cp6-p09-provisioner";
    private const string UnauthorizedAppId = "cp6-p09-unauthorized-probe";
    private const string ExpectedEventType = "com.gtx537.platform.contract-example.changed.v1";
    private const string OrchestrationImageProperty = "kubectlImage";
    private const string ExpectedOrchestrationImage = "registry.k8s.io/kubectl:v1.34.1";
    private const string OrchestrationImageCheck = "kubectl-image";
    private const string ContainerSocketProperty = "dockerSocket";

    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "environmentClass",
        "profileId",
        "runtime",
        "identities",
        "components",
        "topic",
        "acls",
        "compose",
        "kubernetes",
        "evidence"
    ];

    private static readonly string[] RuntimeProperties =
    [
        "daprImage",
        "kafkaImage",
        OrchestrationImageProperty
    ];

    private static readonly string[] IdentityProperties =
    [
        "publisherAppId",
        "receiverAppId",
        "provisionerPrincipal",
        "unauthorizedAppId",
        "consumerGroup"
    ];

    private static readonly string[] ComponentProperties =
    [
        "name",
        "direction",
        "scope",
        "usernameSecretRef",
        "passwordSecretRef"
    ];

    private static readonly string[] TopicProperties =
    [
        "name",
        "eventType",
        "partitions",
        "retentionMs",
        "maxMessageBytes"
    ];

    private static readonly string[] AclProperties =
    [
        "principal",
        "resourceType",
        "resourceName",
        "operation"
    ];

    private static readonly string[] ComposeProperties =
    [
        "appNetwork",
        "runtimeNetwork",
        "bootstrapServers",
        "kafkaHostPort",
        "hostBinding",
        "hostNetwork",
        "privileged",
        ContainerSocketProperty,
        "hostPath"
    ];

    private static readonly string[] ClusterProperties =
    [
        "namespace",
        "nonDeployableLabel",
        "defaultDeny",
        "dnsEgress",
        "minimalProbeIngress",
        "minimalKafkaEgress",
        "forbiddenKinds"
    ];

    private static readonly string[] EvidenceProperties =
    [
        "schemaId",
        "requiredChecks"
    ];

    private static readonly string[] ForbiddenKinds =
    [
        "Secret",
        "ClusterRole",
        "ClusterRoleBinding",
        "Ingress",
        "PersistentVolume"
    ];

    private static readonly string[] RequiredChecks =
    [
        "profile-valid",
        "provision-first",
        "provision-idempotent",
        "invoke-positive",
        "pubsub-positive",
        "direct-kafka-denied",
        "principal-denied",
        "appid-scope-denied",
        "foreign-topic-denied",
        "kubernetes-render",
        "kubernetes-policy",
        "zero-residue"
    ];

    private static readonly ExpectedAcl[] ExpectedAcls =
    [
        new(PublisherAppId, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Write"),
        new(PublisherAppId, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Describe"),
        new(ReceiverAppId, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Read"),
        new(ReceiverAppId, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Describe"),
        new(ReceiverAppId, "Group", Cp6P09RuntimeProfile.ExpectedConsumerGroup, "Read"),
        new(ProvisionerPrincipal, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Create"),
        new(ProvisionerPrincipal, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Alter"),
        new(ProvisionerPrincipal, "Topic", Cp6P09RuntimeProfile.ExpectedTopic, "Describe"),
        new(ProvisionerPrincipal, "Cluster", "kafka-cluster", "Describe")
    ];

    internal static void Validate(JsonElement root)
    {
        ValidateStringsAndSecretFields(root);
        RequireExactObject(root, RootProperties);

        ExpectString(root, "schemaVersion", "1", "schema-version");
        ExpectString(root, "environmentClass", "NonProduction", "production-environment");
        ExpectString(root, "profileId", Cp6P09RuntimeProfile.ExpectedProfileId, "profile-id");

        ValidateRuntime(RequireProperty(root, "runtime", JsonValueKind.Object));
        ValidateIdentities(RequireProperty(root, "identities", JsonValueKind.Object));
        ValidateComponents(RequireProperty(root, "components", JsonValueKind.Array));
        ValidateTopic(RequireProperty(root, "topic", JsonValueKind.Object));
        ValidateAcls(RequireProperty(root, "acls", JsonValueKind.Array));
        ValidateCompose(RequireProperty(root, "compose", JsonValueKind.Object));
        ValidateCluster(RequireProperty(root, "kubernetes", JsonValueKind.Object));
        ValidateEvidence(RequireProperty(root, "evidence", JsonValueKind.Object));
    }

    private static void ValidateRuntime(JsonElement runtime)
    {
        RequireExactObject(runtime, RuntimeProperties);
        ExpectString(runtime, "daprImage", "daprio/daprd:1.18.2", "floating-dapr-image");
        ExpectString(runtime, "kafkaImage", "apache/kafka:4.3.1", "floating-kafka-image");
        ExpectString(runtime, OrchestrationImageProperty, ExpectedOrchestrationImage, OrchestrationImageCheck);
    }

    private static void ValidateIdentities(JsonElement identities)
    {
        RequireExactObject(identities, IdentityProperties);
        ExpectString(identities, "publisherAppId", PublisherAppId, "crm-app-id");
        ExpectString(identities, "receiverAppId", ReceiverAppId, "crm-app-id");
        ExpectString(identities, "provisionerPrincipal", ProvisionerPrincipal, "crm-app-id");
        ExpectString(identities, "unauthorizedAppId", UnauthorizedAppId, "crm-app-id");
        ExpectString(identities, "consumerGroup", Cp6P09RuntimeProfile.ExpectedConsumerGroup, "consumer-group");
    }

    private static void ValidateComponents(JsonElement components)
    {
        foreach (var component in components.EnumerateArray())
        {
            RequireExactObject(component, ComponentProperties);
            _ = RequireProperty(component, "name", JsonValueKind.String);
            _ = RequireProperty(component, "direction", JsonValueKind.String);
            var scope = RequireProperty(component, "scope", JsonValueKind.Array);
            foreach (var item in scope.EnumerateArray())
            {
                RequireKind(item, JsonValueKind.String);
            }

            _ = RequireProperty(component, "usernameSecretRef", JsonValueKind.String);
            _ = RequireProperty(component, "passwordSecretRef", JsonValueKind.String);
        }

        if (components.GetArrayLength() != 2)
        {
            Fail("component-mismatch", "The component list must contain exactly the approved publish and subscribe components.");
        }

        ValidateComponent(
            components[0],
            "cp6-p09-kafka-publish",
            "Publish",
            PublisherAppId,
            "publisher-username",
            "publisher-password");
        ValidateComponent(
            components[1],
            "cp6-p09-kafka-subscribe",
            "Subscribe",
            ReceiverAppId,
            "receiver-username",
            "receiver-password");
    }

    private static void ValidateComponent(
        JsonElement component,
        string name,
        string direction,
        string scopedAppId,
        string usernameReference,
        string passwordReference)
    {
        ExpectString(component, "name", name, "component-mismatch");
        ExpectString(component, "direction", direction, "component-mismatch");
        ExpectString(component, "usernameSecretRef", usernameReference, "component-mismatch");
        ExpectString(component, "passwordSecretRef", passwordReference, "component-mismatch");

        var scope = component.GetProperty("scope");
        if (scope.GetArrayLength() != 1 || !StringEquals(scope[0], scopedAppId))
        {
            Fail("component-scope", "A component scope differs from its single approved application identity.");
        }
    }

    private static void ValidateTopic(JsonElement topic)
    {
        RequireExactObject(topic, TopicProperties);
        ExpectString(topic, "name", Cp6P09RuntimeProfile.ExpectedTopic, "crm-topic");
        ExpectString(topic, "eventType", ExpectedEventType, "event-type");
        ExpectInteger(topic, "partitions", 3, "wrong-partitions");
        ExpectInteger(topic, "retentionMs", 3_600_000, "topic-retention");
        ExpectInteger(topic, "maxMessageBytes", 1_048_576, "topic-message-size");
    }

    private static void ValidateAcls(JsonElement acls)
    {
        var actual = new List<ExpectedAcl>();
        foreach (var acl in acls.EnumerateArray())
        {
            RequireExactObject(acl, AclProperties);
            actual.Add(new ExpectedAcl(
                RequireString(acl, "principal"),
                RequireString(acl, "resourceType"),
                RequireString(acl, "resourceName"),
                RequireString(acl, "operation")));
        }

        if (actual.Any(acl =>
                string.Equals(acl.Principal, ReceiverAppId, StringComparison.Ordinal) &&
                string.Equals(acl.Operation, "Write", StringComparison.Ordinal)))
        {
            Fail("write-on-receiver", "The receiver identity cannot have a write operation.");
        }

        if (!actual.SequenceEqual(ExpectedAcls))
        {
            Fail("acl-mismatch", "The ACL list differs from the exact ordered least-privilege tuples.");
        }
    }

    private static void ValidateCompose(JsonElement compose)
    {
        RequireExactObject(compose, ComposeProperties);
        ExpectString(compose, "appNetwork", "app", "compose-network");
        ExpectString(compose, "runtimeNetwork", "runtime", "compose-network");
        ExpectString(compose, "bootstrapServers", "kafka:9092", "external-host");
        ExpectBoolean(compose, "kafkaHostPort", false, "kafka-host-port");
        ExpectString(compose, "hostBinding", "127.0.0.1:0", "fixed-public-port");
        ExpectBoolean(compose, "hostNetwork", false, "host-network");
        ExpectBoolean(compose, "privileged", false, "privileged-runtime");
        ExpectBoolean(compose, ContainerSocketProperty, false, "container-socket");
        ExpectBoolean(compose, "hostPath", false, "host-path");
    }

    private static void ValidateCluster(JsonElement cluster)
    {
        RequireExactObject(cluster, ClusterProperties);
        ExpectString(cluster, "namespace", "cp6-p09-ci", "cluster-namespace");
        ExpectString(cluster, "nonDeployableLabel", "cp6.io/nondeployable=true", "nondeployable-label");
        ExpectBoolean(cluster, "defaultDeny", true, "cluster-policy");
        ExpectBoolean(cluster, "dnsEgress", true, "cluster-policy");
        ExpectBoolean(cluster, "minimalProbeIngress", true, "cluster-policy");
        ExpectBoolean(cluster, "minimalKafkaEgress", true, "cluster-policy");
        ExpectOrderedStrings(cluster, "forbiddenKinds", ForbiddenKinds, "forbidden-kinds");
    }

    private static void ValidateEvidence(JsonElement evidence)
    {
        RequireExactObject(evidence, EvidenceProperties);
        ExpectString(
            evidence,
            "schemaId",
            "https://cp6.example/contracts/p09/rehearsal-evidence.v1.schema.json",
            "evidence-schema");
        ExpectOrderedStrings(evidence, "requiredChecks", RequiredChecks, "required-checks");
    }

    private static void ExpectOrderedStrings(
        JsonElement parent,
        string propertyName,
        IReadOnlyList<string> expected,
        string checkId)
    {
        var values = RequireProperty(parent, propertyName, JsonValueKind.Array);
        var actual = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            RequireKind(value, JsonValueKind.String);
            actual.Add(value.GetString()!);
        }

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            Fail(checkId, $"Property '{propertyName}' differs from the exact approved ordered values.");
        }
    }

    private static void ValidateStringsAndSecretFields(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateString(property.Name);
                    if (property.Name.Equals("password", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("connectionString", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("secretValue", StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("plaintext-secret", $"Property '{property.Name}' is a forbidden plaintext secret field.");
                    }

                    ValidateStringsAndSecretFields(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateStringsAndSecretFields(item);
                }

                break;
            case JsonValueKind.String:
                ValidateString(element.GetString()!);
                break;
        }
    }

    private static void ValidateString(string value)
    {
        if (value.Length == 0 ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\0', StringComparison.Ordinal) ||
            !value.IsNormalized(NormalizationForm.FormC))
        {
            Fail("invalid-string", "All property names and string values must be non-empty NFC without carriage returns or NUL characters.");
        }
    }

    private static void RequireExactObject(JsonElement element, IReadOnlyCollection<string> expectedProperties)
    {
        RequireKind(element, JsonValueKind.Object);
        var allowed = new HashSet<string>(expectedProperties, StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                Fail("unknown-property", $"Property '{property.Name}' is not allowed in this object.");
            }
        }

        foreach (var propertyName in expectedProperties)
        {
            if (!element.TryGetProperty(propertyName, out _))
            {
                Fail("missing-property", $"Required property '{propertyName}' is missing.");
            }
        }
    }

    private static JsonElement RequireProperty(JsonElement parent, string propertyName, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            Fail("missing-property", $"Required property '{propertyName}' is missing.");
        }

        RequireKind(property, kind);
        return property;
    }

    private static string RequireString(JsonElement parent, string propertyName) =>
        RequireProperty(parent, propertyName, JsonValueKind.String).GetString()!;

    private static void ExpectString(JsonElement parent, string propertyName, string expected, string checkId)
    {
        var actual = RequireString(parent, propertyName);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Fail(checkId, $"Property '{propertyName}' does not have its approved fixed value.");
        }
    }

    private static bool StringEquals(JsonElement value, string expected) =>
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static void ExpectInteger(JsonElement parent, string propertyName, int expected, string checkId)
    {
        var value = RequireProperty(parent, propertyName, JsonValueKind.Number);
        if (!value.TryGetInt32(out var actual))
        {
            Fail("wrong-type", $"Property '{propertyName}' must be a 32-bit integer.");
        }

        if (actual != expected)
        {
            Fail(checkId, $"Property '{propertyName}' does not have its approved fixed value.");
        }
    }

    private static void ExpectBoolean(JsonElement parent, string propertyName, bool expected, string checkId)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            Fail("missing-property", $"Required property '{propertyName}' is missing.");
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            Fail("wrong-type", $"Property '{propertyName}' must be a Boolean.");
        }

        if (value.GetBoolean() != expected)
        {
            Fail(checkId, $"Property '{propertyName}' does not have its approved fixed value.");
        }
    }

    private static void RequireKind(JsonElement element, JsonValueKind expected)
    {
        if (element.ValueKind != expected)
        {
            Fail("wrong-type", $"Expected JSON kind {expected} but found {element.ValueKind}.");
        }
    }

    private static void Fail(string checkId, string message) => throw new Cp6P09ContractException(checkId, message);

    private readonly record struct ExpectedAcl(
        string Principal,
        string ResourceType,
        string ResourceName,
        string Operation);
}
