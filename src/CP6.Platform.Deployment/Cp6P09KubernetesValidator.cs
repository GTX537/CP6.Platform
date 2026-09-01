using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CP6.Platform.Deployment;

public sealed record Cp6P09KubernetesValidationResult(string CanonicalJson, string Sha256);

public static class Cp6P09KubernetesValidator
{
    public const string ExpectedNamespace = "cp6-p09-ci";

    private const string NondeployableLabel = "cp6.io/nondeployable";
    private const string FixtureImagePrefix = "example.invalid/cp6/p09-fixture@sha256:";

    private static readonly Regex DigestImage = new(
        "\\A[^\\s@:]+(?:/[^\\s@:]+)*/[^\\s@:]+@sha256:[0-9a-f]{64}\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex FixtureImage = new(
        "\\Aexample\\.invalid/cp6/p09-fixture@sha256:[0-9a-f]{64}\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex MachinePath = new(
        @"(?:\A[A-Za-z]:[\\/]|\A\\\\|\A/(?:Users|home|var/folders)/)",
        RegexOptions.CultureInvariant);

    private static readonly Regex SecretAssignment = new(
        @"(?i)(?:password|secret|token|api[-_]?key)\s*[:=]\s*[^$\s]+|-----BEGIN [A-Z ]*PRIVATE KEY-----|\AAKIA[0-9A-Z]{16}\z",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedResources = new(StringComparer.Ordinal)
    {
        "v1|Namespace",
        "v1|ServiceAccount",
        "v1|ConfigMap",
        "apps/v1|Deployment",
        "v1|Service",
        "batch/v1|Job",
        "dapr.io/v1alpha1|Component",
        "dapr.io/v2alpha1|Subscription",
        "networking.k8s.io/v1|NetworkPolicy"
    };

    private static readonly HashSet<string> RequiredServiceAccounts = new(StringComparer.Ordinal)
    {
        "publisher",
        "receiver",
        "provisioner",
        "unauthorized"
    };

    private static readonly HashSet<string> RequiredPolicies = new(StringComparer.Ordinal)
    {
        "default-deny",
        "allow-dns-egress",
        "allow-probe-ingress",
        "allow-publisher-kafka-egress",
        "allow-receiver-kafka-egress",
        "allow-provisioner-kafka-egress"
    };

    public static Cp6P09KubernetesValidationResult Validate(
        Cp6P09RuntimeProfile profile,
        IEnumerable<string> resources)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(resources);

        var parsed = new List<Resource>();
        foreach (var json in resources)
        {
            if (json is null)
            {
                Fail("k8s-json", "A Kubernetes resource JSON value is null.");
            }

            string canonical;
            JsonObject root;
            try
            {
                canonical = Cp6P09Json.Canonicalize(json!);
                root = JsonNode.Parse(canonical)?.AsObject()
                    ?? throw new InvalidOperationException("The resource root is not an object.");
            }
            catch (Exception exception) when (exception is Cp6P09ContractException or InvalidOperationException)
            {
                throw new Cp6P09ContractException(
                    "k8s-json",
                    "A Kubernetes resource is not a strict JSON object.",
                    exception);
            }

            var apiVersion = RequiredString(root, "apiVersion", "k8s-kind");
            var kind = RequiredString(root, "kind", "k8s-kind");
            if (!AllowedResources.Contains($"{apiVersion}|{kind}"))
            {
                Fail("k8s-kind", $"Kubernetes resource kind '{apiVersion}/{kind}' is not allowed.");
            }

            var metadata = RequiredObject(root, "metadata", "k8s-identity");
            var name = RequiredString(metadata, "name", "k8s-identity");
            var @namespace = metadata["namespace"]?.GetValue<string>() ?? string.Empty;
            if (kind == "Namespace")
            {
                if (!string.Equals(name, ExpectedNamespace, StringComparison.Ordinal) || @namespace.Length != 0)
                {
                    Fail("k8s-namespace", "The Namespace identity is not the frozen P09 namespace.");
                }
            }
            else if (!string.Equals(@namespace, ExpectedNamespace, StringComparison.Ordinal))
            {
                Fail("k8s-namespace", $"Resource '{kind}/{name}' is outside the frozen P09 namespace.");
            }

            var labels = RequiredObject(metadata, "labels", "k8s-nondeployable-label");
            if (!string.Equals(labels[NondeployableLabel]?.GetValue<string>(), "true", StringComparison.Ordinal))
            {
                Fail("k8s-nondeployable-label", $"Resource '{kind}/{name}' is missing the nondeployable label.");
            }

            ValidateProhibitedFields(root);
            ValidateStringBoundary(root);
            parsed.Add(new Resource(apiVersion, kind, @namespace, name, canonical, root));
        }

        if (parsed.Count == 0)
        {
            Fail("k8s-object-set", "The Kubernetes object set is empty.");
        }

        var duplicate = parsed.GroupBy(resource => resource.Identity, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            Fail("k8s-identity", $"Duplicate Kubernetes identity '{duplicate.Key}'.");
        }

        ValidateServiceAccounts(parsed);
        ValidateWorkloadsAndServices(parsed);
        ValidateProfileConfigMap(profile, parsed);
        ValidateDapr(profile, parsed);
        ValidateNetworkPolicies(parsed);

        var canonicalSet = $"[{string.Join(',', parsed.OrderBy(resource => resource.Identity, StringComparer.Ordinal).Select(resource => resource.Canonical))}]";
        return new Cp6P09KubernetesValidationResult(
            canonicalSet,
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(canonicalSet)));
    }

    private static void ValidateServiceAccounts(IReadOnlyCollection<Resource> resources)
    {
        var actual = resources.Where(resource => resource.Kind == "ServiceAccount")
            .Select(resource => resource.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(RequiredServiceAccounts))
        {
            Fail("k8s-service-account", "The P09 ServiceAccount set is not exact.");
        }
    }

    private static void ValidateWorkloadsAndServices(IReadOnlyCollection<Resource> resources)
    {
        var serviceAccounts = resources.Where(resource => resource.Kind == "ServiceAccount")
            .Select(resource => resource.Name)
            .ToHashSet(StringComparer.Ordinal);
        var workloads = resources.Where(resource => resource.Kind is "Deployment" or "Job")
            .Select(CreateWorkload)
            .ToArray();

        foreach (var workload in workloads)
        {
            if (!serviceAccounts.Contains(workload.ServiceAccountName))
            {
                Fail("k8s-service-account", $"Workload '{workload.Name}' references an unknown ServiceAccount.");
            }

            var templateLabels = RequiredObject(
                RequiredObject(
                    RequiredObject(workload.Resource.Root, "spec", "k8s-selector"),
                    "template",
                    "k8s-selector"),
                "metadata",
                "k8s-selector");
            var labels = RequiredObject(templateLabels, "labels", "k8s-selector");
            if (!string.Equals(labels[NondeployableLabel]?.GetValue<string>(), "true", StringComparison.Ordinal))
            {
                Fail("k8s-nondeployable-label", $"Workload template '{workload.Name}' is missing the nondeployable label.");
            }

            foreach (var container in workload.Containers)
            {
                var image = RequiredString(container, "image", "k8s-image-digest");
                if (!DigestImage.IsMatch(image))
                {
                    Fail("k8s-image-digest", $"Workload '{workload.Name}' does not use a digest image.");
                }

                if (!FixtureImage.IsMatch(image) || !image.StartsWith(FixtureImagePrefix, StringComparison.Ordinal))
                {
                    Fail("k8s-image-registry", $"Workload '{workload.Name}' does not use the bounded fixture registry.");
                }
            }

            if (workload.Resource.Kind == "Deployment")
            {
                var selector = RequiredObject(
                    RequiredObject(workload.Resource.Root, "spec", "k8s-selector"),
                    "selector",
                    "k8s-selector");
                AssertSelectorResolves(selector, workloads, [workload.Name], "k8s-selector");
            }

            if (workload.Name is "publisher" or "receiver")
            {
                foreach (var container in workload.Containers)
                {
                    foreach (var value in EnumerateStrings(container))
                    {
                        if (value.Contains("kafka", StringComparison.OrdinalIgnoreCase))
                        {
                            Fail("k8s-app-kafka-address", "An application container contains a direct Kafka address or setting.");
                        }
                    }
                }
            }
        }

        var services = resources.Where(resource => resource.Kind == "Service").ToArray();
        if (!services.Select(service => service.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["publisher", "receiver", "kafka"]))
        {
            Fail("k8s-service", "The P09 Service set is not exact.");
        }

        foreach (var service in services)
        {
            var spec = RequiredObject(service.Root, "spec", "k8s-service-type");
            if (!string.Equals(spec["type"]?.GetValue<string>(), "ClusterIP", StringComparison.Ordinal))
            {
                Fail("k8s-service-type", $"Service '{service.Name}' is not ClusterIP-only.");
            }

            var selector = RequiredObject(spec, "selector", "k8s-selector");
            AssertSelectorResolves(
                new JsonObject { ["matchLabels"] = selector.DeepClone() },
                workloads,
                [service.Name],
                "k8s-selector");
        }
    }

    private static void ValidateProfileConfigMap(
        Cp6P09RuntimeProfile profile,
        IReadOnlyCollection<Resource> resources)
    {
        var config = Find(resources, "ConfigMap", "p09-runtime-profile", "k8s-profile");
        var data = RequiredObject(config.Root, "data", "k8s-profile");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileId"] = profile.ProfileId,
            ["profileSha256"] = profile.Sha256,
            ["environmentClass"] = profile.EnvironmentClass,
            ["topicName"] = profile.TopicName,
            ["eventType"] = profile.EventType,
            ["partitions"] = profile.Partitions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["publisherAppId"] = profile.PublisherAppId,
            ["receiverAppId"] = profile.ReceiverAppId,
            ["unauthorizedAppId"] = profile.UnauthorizedAppId,
            ["publishComponentName"] = profile.PublishComponentName,
            ["subscribeComponentName"] = profile.SubscribeComponentName,
            ["subscriptionRoute"] = "/events/p09"
        };
        foreach (var pair in expected)
        {
            if (!string.Equals(data[pair.Key]?.GetValue<string>(), pair.Value, StringComparison.Ordinal))
            {
                Fail("k8s-profile", $"Profile ConfigMap field '{pair.Key}' is not exact.");
            }
        }

        var executionMode = data["executionMode"]?.GetValue<string>();
        if (executionMode is not ("offline" or "ci"))
        {
            Fail("k8s-profile", "Profile ConfigMap executionMode is not recognized.");
        }
    }

    private static void ValidateDapr(
        Cp6P09RuntimeProfile profile,
        IReadOnlyCollection<Resource> resources)
    {
        var components = resources.Where(resource => resource.Kind == "Component").ToArray();
        if (components.Length != 2)
        {
            Fail("k8s-component", "Exactly two Dapr Components are required.");
        }

        ValidateComponent(
            Find(resources, "Component", profile.PublishComponentName, "k8s-component"),
            profile.PublisherAppId,
            consumerGroup: null);
        ValidateComponent(
            Find(resources, "Component", profile.SubscribeComponentName, "k8s-component"),
            profile.ReceiverAppId,
            Cp6P09RuntimeProfile.ExpectedConsumerGroup);

        var subscription = Find(
            resources,
            "Subscription",
            "cp6-p09-deployment-probe-subscription",
            "k8s-subscription");
        var spec = RequiredObject(subscription.Root, "spec", "k8s-subscription");
        if (!string.Equals(spec["pubsubname"]?.GetValue<string>(), profile.SubscribeComponentName, StringComparison.Ordinal))
        {
            Fail("k8s-subscription-component", "The Subscription does not use the subscribe-only component.");
        }

        if (!string.Equals(spec["topic"]?.GetValue<string>(), profile.TopicName, StringComparison.Ordinal) ||
            !string.Equals(spec["routes"]?["default"]?.GetValue<string>(), "/events/p09", StringComparison.Ordinal) ||
            !ExactStringArray(spec["scopes"], [profile.ReceiverAppId]))
        {
            Fail("k8s-subscription", "The Dapr Subscription topic, route, or AppId scope is not exact.");
        }
    }

    private static void ValidateComponent(Resource component, string expectedScope, string? consumerGroup)
    {
        if (!ExactStringArray(component.Root["scopes"], [expectedScope]))
        {
            Fail("k8s-component-scope", $"Component '{component.Name}' has an invalid scope.");
        }

        var spec = RequiredObject(component.Root, "spec", "k8s-component");
        if (!string.Equals(spec["type"]?.GetValue<string>(), "pubsub.kafka", StringComparison.Ordinal) ||
            !string.Equals(spec["version"]?.GetValue<string>(), "v1", StringComparison.Ordinal))
        {
            Fail("k8s-component", $"Component '{component.Name}' has an invalid type or version.");
        }

        var metadata = spec["metadata"]?.AsArray() ?? Fail<JsonArray>("k8s-component", "Component metadata is missing.");
        AssertMetadataValue(metadata, "brokers", "kafka:9092");
        AssertMetadataValue(metadata, "authType", "password");
        var publishComponent = component.Name.EndsWith("-publish", StringComparison.Ordinal);
        AssertSecretReference(
            metadata,
            "saslUsername",
            publishComponent ? "publisher-username" : "receiver-username");
        AssertSecretReference(
            metadata,
            "saslPassword",
            publishComponent ? "publisher-password" : "receiver-password");
        var consumerEntries = metadata.Where(entry => entry?["name"]?.GetValue<string>() == "consumerGroup").ToArray();
        if (consumerGroup is null)
        {
            if (consumerEntries.Length != 0)
            {
                Fail("k8s-component", "The publish component must not define a consumer group.");
            }
        }
        else if (consumerEntries.Length != 1 ||
            !string.Equals(consumerEntries[0]?["value"]?.GetValue<string>(), consumerGroup, StringComparison.Ordinal))
        {
            Fail("k8s-component", "The subscribe component consumer group is not exact.");
        }
    }

    private static void ValidateNetworkPolicies(IReadOnlyCollection<Resource> resources)
    {
        var policies = resources.Where(resource => resource.Kind == "NetworkPolicy").ToArray();
        if (!policies.Select(policy => policy.Name).ToHashSet(StringComparer.Ordinal).SetEquals(RequiredPolicies))
        {
            Fail("k8s-default-deny", "The P09 NetworkPolicy set, including default deny, is not exact.");
        }

        foreach (var policy in policies)
        {
            if (EnumerateStrings(policy.Root).Any(value => string.Equals(value, "0.0.0.0/0", StringComparison.Ordinal)))
            {
                Fail("k8s-egress-cidr", "World-open egress is prohibited.");
            }

            var targetSelector = RequiredObject(
                RequiredObject(policy.Root, "spec", "k8s-policy"),
                "podSelector",
                "k8s-policy");
            if (SelectorHasLabel(targetSelector, "cp6.io/component", "application"))
            {
                Fail("k8s-app-kafka-egress", "Kafka egress must not target application containers.");
            }
        }

        var workloads = resources.Where(resource => resource.Kind is "Deployment" or "Job")
            .Select(CreateWorkload)
            .ToArray();
        ValidateDefaultDeny(Find(resources, "NetworkPolicy", "default-deny", "k8s-default-deny"));
        ValidateDnsPolicy(Find(resources, "NetworkPolicy", "allow-dns-egress", "k8s-policy"));
        ValidateProbePolicy(Find(resources, "NetworkPolicy", "allow-probe-ingress", "k8s-policy"), workloads);
        ValidateKafkaPolicy(
            Find(resources, "NetworkPolicy", "allow-publisher-kafka-egress", "k8s-policy"),
            workloads,
            "publisher",
            "dapr-sidecar");
        ValidateKafkaPolicy(
            Find(resources, "NetworkPolicy", "allow-receiver-kafka-egress", "k8s-policy"),
            workloads,
            "receiver",
            "dapr-sidecar");
        ValidateKafkaPolicy(
            Find(resources, "NetworkPolicy", "allow-provisioner-kafka-egress", "k8s-policy"),
            workloads,
            "provisioner",
            "provisioner");
    }

    private static void ValidateDefaultDeny(Resource policy)
    {
        var spec = RequiredObject(policy.Root, "spec", "k8s-default-deny");
        var selector = RequiredObject(spec, "podSelector", "k8s-default-deny");
        if (selector.Count != 0 || !ExactStringArray(spec["policyTypes"], ["Ingress", "Egress"]) ||
            spec.ContainsKey("ingress") || spec.ContainsKey("egress"))
        {
            Fail("k8s-default-deny", "Default deny must cover both ingress and egress without allow rules.");
        }
    }

    private static void ValidateDnsPolicy(Resource policy)
    {
        var spec = RequiredObject(policy.Root, "spec", "k8s-policy");
        if (RequiredObject(spec, "podSelector", "k8s-policy").Count != 0 ||
            !ExactStringArray(spec["policyTypes"], ["Egress"]))
        {
            Fail("k8s-policy", "DNS egress selector or direction is not exact.");
        }

        var rules = spec["egress"]?.AsArray();
        var rule = rules is { Count: 1 } ? rules[0]?.AsObject() : null;
        var peers = rule?["to"]?.AsArray();
        var peer = peers is { Count: 1 } ? peers[0]?.AsObject() : null;
        if (peer?["namespaceSelector"]?["matchLabels"]?["kubernetes.io/metadata.name"]?.GetValue<string>() != "kube-system" ||
            peer?["podSelector"]?["matchLabels"]?["k8s-app"]?.GetValue<string>() != "kube-dns" ||
            !ExactPorts(rule?["ports"], [("UDP", 53), ("TCP", 53)]))
        {
            Fail("k8s-policy", "DNS egress peers or ports are not exact.");
        }
    }

    private static void ValidateProbePolicy(Resource policy, IReadOnlyCollection<Workload> workloads)
    {
        var spec = RequiredObject(policy.Root, "spec", "k8s-policy");
        var target = RequiredObject(spec, "podSelector", "k8s-policy");
        AssertSelectorResolves(target, workloads, ["publisher", "receiver"], "k8s-selector");
        if (!ExactStringArray(spec["policyTypes"], ["Ingress"]))
        {
            Fail("k8s-policy", "Probe ingress direction is not exact.");
        }

        var rules = spec["ingress"]?.AsArray();
        var rule = rules is { Count: 1 } ? rules[0]?.AsObject() : null;
        var peers = rule?["from"]?.AsArray();
        var selector = peers is { Count: 1 } ? peers[0]?["podSelector"]?.AsObject() : null;
        if (selector is null || !ExactPorts(rule?["ports"], [("TCP", 8080)]))
        {
            Fail("k8s-policy", "Probe ingress peers or ports are not exact.");
        }

        AssertSelectorResolves(selector!, workloads, ["unauthorized"], "k8s-selector");
    }

    private static void ValidateKafkaPolicy(
        Resource policy,
        IReadOnlyCollection<Workload> workloads,
        string sourceWorkload,
        string kafkaClient)
    {
        var spec = RequiredObject(policy.Root, "spec", "k8s-policy");
        var selector = RequiredObject(spec, "podSelector", "k8s-policy");
        if (!SelectorHasLabel(selector, "cp6.io/workload", sourceWorkload) ||
            !SelectorHasLabel(selector, "cp6.io/kafka-client", kafkaClient))
        {
            Fail("k8s-app-kafka-egress", "Kafka egress source selector is not sidecar/provisioner-only.");
        }

        AssertSelectorResolves(selector, workloads, [sourceWorkload], "k8s-selector");
        if (!ExactStringArray(spec["policyTypes"], ["Egress"]))
        {
            Fail("k8s-policy", "Kafka egress direction is not exact.");
        }

        var rules = spec["egress"]?.AsArray();
        var rule = rules is { Count: 1 } ? rules[0]?.AsObject() : null;
        var peers = rule?["to"]?.AsArray();
        var target = peers is { Count: 1 } ? peers[0]?["podSelector"]?.AsObject() : null;
        if (target is null || !ExactPorts(rule?["ports"], [("TCP", 9092)]))
        {
            Fail("k8s-policy", "Kafka egress peers or ports are not exact.");
        }

        AssertSelectorResolves(target!, workloads, ["kafka"], "k8s-selector");
    }

    private static Workload CreateWorkload(Resource resource)
    {
        var spec = RequiredObject(resource.Root, "spec", "k8s-workload");
        var template = RequiredObject(spec, "template", "k8s-workload");
        var templateMetadata = RequiredObject(template, "metadata", "k8s-workload");
        var labels = RequiredObject(templateMetadata, "labels", "k8s-workload");
        var podSpec = RequiredObject(template, "spec", "k8s-workload");
        var serviceAccountName = RequiredString(podSpec, "serviceAccountName", "k8s-service-account");
        var containers = podSpec["containers"]?.AsArray()?.Select(node => node?.AsObject()
                ?? Fail<JsonObject>("k8s-workload", "A workload container is not an object."))
            .ToArray() ?? Fail<JsonObject[]>("k8s-workload", "A workload has no containers.");
        if (containers.Length == 0)
        {
            Fail("k8s-workload", "A workload has no containers.");
        }

        return new Workload(resource.Name, resource, labels, serviceAccountName, containers);
    }

    private static void AssertSelectorResolves(
        JsonObject selector,
        IReadOnlyCollection<Workload> workloads,
        IReadOnlyCollection<string> expectedNames,
        string checkId)
    {
        var matches = workloads.Where(workload => MatchesSelector(workload.Labels, selector))
            .Select(workload => workload.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!matches.SetEquals(expectedNames))
        {
            Fail(checkId, "A Kubernetes selector does not resolve to its exact intended workload set.");
        }
    }

    private static bool MatchesSelector(JsonObject labels, JsonObject selector)
    {
        if (selector["matchLabels"] is JsonObject matchLabels)
        {
            foreach (var pair in matchLabels)
            {
                if (!string.Equals(labels[pair.Key]?.GetValue<string>(), pair.Value?.GetValue<string>(), StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        if (selector["matchExpressions"] is JsonArray expressions)
        {
            foreach (var expressionNode in expressions)
            {
                var expression = expressionNode!.AsObject();
                var key = RequiredString(expression, "key", "k8s-selector");
                var operation = RequiredString(expression, "operator", "k8s-selector");
                var values = expression["values"]?.AsArray()
                    .Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
                    ?? [];
                if (operation != "In" || !values.Contains(labels[key]?.GetValue<string>() ?? string.Empty))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SelectorHasLabel(JsonObject selector, string key, string value) =>
        string.Equals(selector["matchLabels"]?[key]?.GetValue<string>(), value, StringComparison.Ordinal);

    private static bool ExactPorts(JsonNode? node, IReadOnlyCollection<(string Protocol, int Port)> expected)
    {
        if (node is not JsonArray array)
        {
            return false;
        }

        var actual = array.Select(item =>
                $"{item?["protocol"]?.GetValue<string>()}:{item?["port"]?.GetValue<int>()}")
            .ToHashSet(StringComparer.Ordinal);
        return actual.SetEquals(expected.Select(port => $"{port.Protocol}:{port.Port}"));
    }

    private static bool ExactStringArray(JsonNode? node, IReadOnlyCollection<string> expected)
    {
        if (node is not JsonArray array || array.Count != expected.Count)
        {
            return false;
        }

        return array.Select(value => value?.GetValue<string>() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(expected);
    }

    private static void AssertMetadataValue(JsonArray metadata, string name, string expected)
    {
        var entries = metadata.Where(entry => entry?["name"]?.GetValue<string>() == name).ToArray();
        if (entries.Length != 1 || !string.Equals(entries[0]?["value"]?.GetValue<string>(), expected, StringComparison.Ordinal))
        {
            Fail("k8s-component", $"Dapr metadata '{name}' is not exact.");
        }
    }

    private static void AssertSecretReference(JsonArray metadata, string name, string secretName)
    {
        var entries = metadata.Where(entry => entry?["name"]?.GetValue<string>() == name).ToArray();
        var secretKeyRef = entries.Length == 1 ? entries[0]?["secretKeyRef"]?.AsObject() : null;
        if (secretKeyRef is null ||
            !string.Equals(secretKeyRef["name"]?.GetValue<string>(), secretName, StringComparison.Ordinal) ||
            !string.Equals(secretKeyRef["key"]?.GetValue<string>(), "value", StringComparison.Ordinal) ||
            entries[0]!.AsObject().ContainsKey("value"))
        {
            Fail("k8s-component-secret-ref", $"Dapr metadata '{name}' must use the frozen secretKeyRef only.");
        }
    }

    private static void ValidateProhibitedFields(JsonNode node)
    {
        foreach (var propertyName in EnumeratePropertyNames(node))
        {
            var checkId = propertyName switch
            {
                "hostPath" => "k8s-host-path",
                "hostNetwork" => "k8s-host-network",
                "hostPort" => "k8s-host-port",
                "privileged" => "k8s-privileged",
                _ => null
            };
            if (checkId is not null)
            {
                Fail(checkId, $"Prohibited Kubernetes field '{propertyName}' is present.");
            }
        }
    }

    private static void ValidateStringBoundary(JsonNode node)
    {
        foreach (var (propertyName, value) in EnumerateStringProperties(node))
        {
            if (MachinePath.IsMatch(value))
            {
                Fail("k8s-machine-path", "A Kubernetes resource contains a machine-specific path.");
            }

            if (SecretAssignment.IsMatch(value) ||
                (propertyName is "password" or "token" or "clientSecret" or "apiKey" && value.Length >= 8))
            {
                Fail("k8s-secret-value", "A Kubernetes resource contains a secret-like value.");
            }
        }
    }

    private static IReadOnlyCollection<string> EnumeratePropertyNames(JsonNode node)
    {
        var names = new List<string>();
        CollectPropertyNames(node, names);
        return names;
    }

    private static void CollectPropertyNames(JsonNode node, ICollection<string> names)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                names.Add(pair.Key);
                if (pair.Value is not null)
                {
                    CollectPropertyNames(pair.Value, names);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null))
            {
                CollectPropertyNames(item!, names);
            }
        }
    }

    private static IReadOnlyCollection<(string PropertyName, string Value)> EnumerateStringProperties(JsonNode node)
    {
        var properties = new List<(string PropertyName, string Value)>();
        CollectStringProperties(node, properties);
        return properties;
    }

    private static void CollectStringProperties(
        JsonNode node,
        ICollection<(string PropertyName, string Value)> properties)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    properties.Add((pair.Key, text));
                }
                else if (pair.Value is not null)
                {
                    CollectStringProperties(pair.Value, properties);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null))
            {
                CollectStringProperties(item!, properties);
            }
        }
    }

    private static IReadOnlyCollection<string> EnumerateStrings(JsonNode node) =>
        EnumerateStringProperties(node).Select(pair => pair.Value).ToArray();

    private static Resource Find(
        IEnumerable<Resource> resources,
        string kind,
        string name,
        string checkId)
    {
        var matches = resources.Where(resource =>
            resource.Kind == kind && resource.Name == name).ToArray();
        return matches.Length == 1
            ? matches[0]
            : Fail<Resource>(checkId, $"Required Kubernetes resource '{kind}/{name}' is not exact.");
    }

    private static JsonObject RequiredObject(JsonObject parent, string propertyName, string checkId) =>
        parent[propertyName] is JsonObject value
            ? value
            : Fail<JsonObject>(checkId, $"Required object '{propertyName}' is missing.");

    private static string RequiredString(JsonObject parent, string propertyName, string checkId) =>
        parent[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0
            ? text
            : Fail<string>(checkId, $"Required string '{propertyName}' is missing.");

    private static T Fail<T>(string checkId, string message)
    {
        throw new Cp6P09ContractException(checkId, message);
    }

    private static void Fail(string checkId, string message) =>
        throw new Cp6P09ContractException(checkId, message);

    private sealed record Resource(
        string ApiVersion,
        string Kind,
        string Namespace,
        string Name,
        string Canonical,
        JsonObject Root)
    {
        public string Identity => $"{ApiVersion}|{Kind}|{Namespace}|{Name}";
    }

    private sealed record Workload(
        string Name,
        Resource Resource,
        JsonObject Labels,
        string ServiceAccountName,
        JsonObject[] Containers);
}
