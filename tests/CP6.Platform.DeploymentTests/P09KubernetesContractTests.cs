using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09KubernetesContractTests
{
    private static readonly string BaseRoot = Path.Combine(
        P09ContractTestData.RepositoryRoot,
        "deploy",
        "p09",
        "kubernetes",
        "base");

    private static readonly Cp6P09RuntimeProfile Profile = Cp6P09RuntimeProfile.Parse(
        P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json"));

    public static TheoryData<string, string> RejectionCases => new()
    {
        { "Secret", "k8s-kind" },
        { "ClusterRole", "k8s-kind" },
        { "ClusterRoleBinding", "k8s-kind" },
        { "Ingress", "k8s-kind" },
        { "PersistentVolume", "k8s-kind" },
        { "LoadBalancer", "k8s-service-type" },
        { "NodePort", "k8s-service-type" },
        { "hostPath", "k8s-host-path" },
        { "hostNetwork", "k8s-host-network" },
        { "hostPort", "k8s-host-port" },
        { "privileged", "k8s-privileged" },
        { "production-namespace", "k8s-namespace" },
        { "missing-nondeployable-label", "k8s-nondeployable-label" },
        { "missing-default-deny", "k8s-default-deny" },
        { "world-egress", "k8s-egress-cidr" },
        { "app-to-kafka-egress", "k8s-app-kafka-egress" },
        { "unscoped-component", "k8s-component-scope" },
        { "subscription-publish-component", "k8s-subscription-component" },
        { "floating-image", "k8s-image-digest" },
        { "example-invalid-without-digest", "k8s-image-digest" },
        { "secret-value", "k8s-secret-value" },
        { "machine-path", "k8s-machine-path" }
    };

    [Fact]
    public void ValidBaseResources_AreCanonicalAndDeterministic()
    {
        var resources = ReadBaseResources();

        var first = Cp6P09KubernetesValidator.Validate(Profile, resources);
        var second = Cp6P09KubernetesValidator.Validate(Profile, resources.Reverse());

        Assert.Equal(first.CanonicalJson, second.CanonicalJson);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Matches(new Regex("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant), first.Sha256);

        using var canonical = JsonDocument.Parse(first.CanonicalJson);
        Assert.Equal(JsonValueKind.Array, canonical.RootElement.ValueKind);
        Assert.Equal(resources.Length, canonical.RootElement.GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(RejectionCases))]
    public void ForbiddenMutations_FailAtStablePolicyBoundary(string mutation, string expectedCheckId)
    {
        var resources = ReadBaseResources()
            .Select(json => JsonNode.Parse(json)!.AsObject())
            .ToList();
        ApplyMutation(resources, mutation);

        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09KubernetesValidator.Validate(Profile, resources.Select(resource => resource.ToJsonString())));

        Assert.Equal(expectedCheckId, exception.CheckId);
    }

    private static string[] ReadBaseResources()
    {
        Assert.True(Directory.Exists(BaseRoot), "Required P09 Kubernetes base directory is missing.");
        var files = Directory.GetFiles(BaseRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(files);
        return files.Select(File.ReadAllText).ToArray();
    }

    private static void ApplyMutation(List<JsonObject> resources, string mutation)
    {
        switch (mutation)
        {
            case "Secret":
            case "ClusterRole":
            case "ClusterRoleBinding":
            case "Ingress":
            case "PersistentVolume":
                resources.Add(ForbiddenResource(mutation));
                return;
            case "LoadBalancer":
            case "NodePort":
                Find(resources, "Service", "publisher")["spec"]!["type"] = mutation;
                return;
            case "hostPath":
                PodSpec(resources, "Deployment", "publisher")["volumes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "host",
                        ["hostPath"] = new JsonObject { ["path"] = "/tmp" }
                    }
                };
                return;
            case "hostNetwork":
                PodSpec(resources, "Deployment", "publisher")["hostNetwork"] = true;
                return;
            case "hostPort":
                FirstContainer(resources, "Deployment", "publisher")["ports"] = new JsonArray
                {
                    new JsonObject { ["containerPort"] = 8080, ["hostPort"] = 8080 }
                };
                return;
            case "privileged":
                FirstContainer(resources, "Deployment", "publisher")["securityContext"] =
                    new JsonObject { ["privileged"] = true };
                return;
            case "production-namespace":
                Find(resources, "ConfigMap", "p09-runtime-profile")["metadata"]!["namespace"] = "production";
                return;
            case "missing-nondeployable-label":
                Find(resources, "ConfigMap", "p09-runtime-profile")["metadata"]!["labels"]!
                    .AsObject().Remove("cp6.io/nondeployable");
                return;
            case "missing-default-deny":
                resources.Remove(Find(resources, "NetworkPolicy", "default-deny"));
                return;
            case "world-egress":
                Find(resources, "NetworkPolicy", "allow-dns-egress")["spec"]!["egress"]![0]!["to"] =
                    new JsonArray
                    {
                        new JsonObject
                        {
                            ["ipBlock"] = new JsonObject { ["cidr"] = "0.0.0.0/0" }
                        }
                    };
                return;
            case "app-to-kafka-egress":
                Find(resources, "NetworkPolicy", "allow-publisher-kafka-egress")["spec"]!["podSelector"] =
                    new JsonObject
                    {
                        ["matchLabels"] = new JsonObject { ["cp6.io/component"] = "application" }
                    };
                return;
            case "unscoped-component":
                Find(resources, "Component", Profile.PublishComponentName)["scopes"] = new JsonArray();
                return;
            case "subscription-publish-component":
                Find(resources, "Subscription", "cp6-p09-deployment-probe-subscription")["spec"]!["pubsubname"] =
                    Profile.PublishComponentName;
                return;
            case "floating-image":
                FirstContainer(resources, "Deployment", "publisher")["image"] = "ghcr.io/example/p09:latest";
                return;
            case "example-invalid-without-digest":
                FirstContainer(resources, "Deployment", "publisher")["image"] =
                    "example.invalid/cp6/p09-fixture:v1";
                return;
            case "secret-value":
                Find(resources, "ConfigMap", "p09-runtime-profile")["data"]!["password"] = "super-secret-value";
                return;
            case "machine-path":
                Find(resources, "ConfigMap", "p09-runtime-profile")["data"]!["path"] = @"C:\\Users\\developer\\p09";
                return;
            default:
                throw new InvalidOperationException($"Unknown Kubernetes mutation '{mutation}'.");
        }
    }

    private static JsonObject ForbiddenResource(string kind)
    {
        var apiVersion = kind switch
        {
            "Ingress" => "networking.k8s.io/v1",
            "ClusterRole" or "ClusterRoleBinding" => "rbac.authorization.k8s.io/v1",
            _ => "v1"
        };
        return new JsonObject
        {
            ["apiVersion"] = apiVersion,
            ["kind"] = kind,
            ["metadata"] = new JsonObject
            {
                ["name"] = $"forbidden-{kind.ToLowerInvariant()}",
                ["namespace"] = Cp6P09KubernetesValidator.ExpectedNamespace,
                ["labels"] = new JsonObject { ["cp6.io/nondeployable"] = "true" }
            }
        };
    }

    private static JsonObject Find(IEnumerable<JsonObject> resources, string kind, string name) =>
        resources.Single(resource =>
            string.Equals(resource["kind"]?.GetValue<string>(), kind, StringComparison.Ordinal) &&
            string.Equals(resource["metadata"]?["name"]?.GetValue<string>(), name, StringComparison.Ordinal));

    private static JsonObject PodSpec(
        IEnumerable<JsonObject> resources,
        string kind,
        string name) =>
        Find(resources, kind, name)["spec"]!["template"]!["spec"]!.AsObject();

    private static JsonObject FirstContainer(
        IEnumerable<JsonObject> resources,
        string kind,
        string name) =>
        PodSpec(resources, kind, name)["containers"]![0]!.AsObject();
}
