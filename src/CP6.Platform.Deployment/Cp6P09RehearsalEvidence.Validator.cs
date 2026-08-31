using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Deployment;

public sealed partial class Cp6P09RehearsalEvidence
{
    private const string ExpectedRepositoryVersion = "0.9.0.0";
    private const string ExpectedPackageVersion = "0.9.0-alpha.1";
    private const string ExpectedEventType = "com.gtx537.platform.contract-example.changed.v1";
    private const string PublisherAppId = "cp6-p09-probe-publisher";
    private const string ReceiverAppId = "cp6-p09-probe-receiver";
    private const string ProvisionerPrincipal = "cp6-p09-provisioner";

    private static readonly Regex Sha256Pattern = SafeRegex("^[0-9a-f]{64}$");
    private static readonly Regex GitShaPattern = SafeRegex("^[0-9a-f]{40}$");
    private static readonly Regex DigestPattern = SafeRegex("^sha256:[0-9a-f]{64}$");
    private static readonly Regex TracePattern = SafeRegex("^(?!0{32}$)[0-9a-f]{32}$");
    private static readonly Regex SpanPattern = SafeRegex("^(?!0{16}$)[0-9a-f]{16}$");
    private static readonly Regex IdentifierPattern = SafeRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");
    private static readonly Regex UtcPattern = SafeRegex(
        "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d{1,7})?Z$");

    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "profileId",
        "profileSha256",
        "platformGitSha",
        "repositoryVersion",
        "packageVersion",
        "composeManifestSha256",
        "kubernetesManifestSha256",
        "runtime",
        "topic",
        "acls",
        "checks",
        "trace",
        "startedUtc",
        "completedUtc",
        "teardown",
        "overall"
    ];

    private static readonly string[] RuntimeProperties =
    [
        "daprImage",
        "daprImageDigest",
        "kafkaImage",
        "kafkaImageDigest",
        "kubectlImage",
        "kubectlImageDigest",
        "kubectlVersion"
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

    private static readonly string[] CheckProperties = ["id", "result", "summary"];

    private static readonly string[] TraceProperties =
    [
        "eventId",
        "eventType",
        "topic",
        "partitionKey",
        "traceId",
        "publisherSpanId",
        "receiverSpanId",
        "invocationTraceId",
        "invokerSpanId",
        "invokedSpanId"
    ];

    private static readonly string[] TeardownProperties =
    [
        "commandExitCode",
        "containerCount",
        "networkCount",
        "volumeCount",
        "imageCount",
        "temporaryDirectoryRemoved"
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

    private static Cp6P09RehearsalEvidence CreateValidated(byte[] canonical, JsonElement root)
    {
        RequireExactObject(root, RootProperties);

        var schemaVersion = ExpectString(root, "schemaVersion", "1", "schema-version");
        var profileId = ExpectString(root, "profileId", Cp6P09RuntimeProfile.ExpectedProfileId, "profile-id");
        var profileSha256 = RequirePattern(root, "profileSha256", Sha256Pattern, "invalid-hash");
        if (!string.Equals(profileSha256, Cp6P09RuntimeProfile.ExpectedSha256, StringComparison.Ordinal))
        {
            Fail("profile-mismatch", "Evidence does not identify the frozen canonical runtime Profile.");
        }

        var platformGitSha = RequirePattern(root, "platformGitSha", GitShaPattern, "invalid-hash");
        var repositoryVersion = ExpectString(root, "repositoryVersion", ExpectedRepositoryVersion, "repository-version");
        var packageVersion = ExpectString(root, "packageVersion", ExpectedPackageVersion, "package-version");
        _ = RequirePattern(root, "composeManifestSha256", Sha256Pattern, "invalid-hash");
        _ = RequirePattern(root, "kubernetesManifestSha256", Sha256Pattern, "invalid-hash");

        ValidateRuntime(RequireProperty(root, "runtime", JsonValueKind.Object));
        ValidateTopic(RequireProperty(root, "topic", JsonValueKind.Object));
        ValidateAcls(RequireProperty(root, "acls", JsonValueKind.Array));
        var checks = ValidateChecks(RequireProperty(root, "checks", JsonValueKind.Array));
        ValidateTrace(RequireProperty(root, "trace", JsonValueKind.Object));
        var startedUtc = RequireUtc(root, "startedUtc");
        var completedUtc = RequireUtc(root, "completedUtc");
        if (completedUtc < startedUtc)
        {
            Fail("invalid-time", "Evidence completion time cannot precede its start time.");
        }

        var teardown = ValidateTeardown(RequireProperty(root, "teardown", JsonValueKind.Object));
        var overall = RequireString(root, "overall");
        if (overall is not ("Passed" or "Failed"))
        {
            Fail("overall", "Evidence overall result must be Passed or Failed.");
        }

        var checkIds = checks.Select(check => check.Id).ToArray();
        if (!checkIds.SequenceEqual(Cp6P09RuntimeProfileValidator.ExpectedRequiredChecks, StringComparer.Ordinal))
        {
            Fail("required-checks", "Evidence must contain the exact ordered Profile checks.");
        }

        if (overall == "Passed" &&
            (checks.Any(check => check.Result != "Passed") ||
             teardown.CommandExitCode != 0 ||
             !teardown.TemporaryDirectoryRemoved ||
             teardown.ResourceCount != 0))
        {
            Fail("false-pass", "Passed evidence requires all checks and zero residue.");
        }

        return new Cp6P09RehearsalEvidence(
            canonical,
            schemaVersion,
            profileId,
            profileSha256,
            platformGitSha,
            repositoryVersion,
            packageVersion,
            overall,
            startedUtc,
            completedUtc,
            checks,
            teardown);
    }

    private static void ValidateRuntime(JsonElement runtime)
    {
        RequireExactObject(runtime, RuntimeProperties);
        _ = ExpectString(runtime, "daprImage", "daprio/daprd:1.18.2", "runtime-image");
        _ = RequirePattern(runtime, "daprImageDigest", DigestPattern, "invalid-hash");
        _ = ExpectString(runtime, "kafkaImage", "apache/kafka:4.3.1", "runtime-image");
        _ = RequirePattern(runtime, "kafkaImageDigest", DigestPattern, "invalid-hash");
        _ = ExpectString(runtime, "kubectlImage", "registry.k8s.io/kubectl:v1.34.1", "runtime-image");
        _ = RequirePattern(runtime, "kubectlImageDigest", DigestPattern, "invalid-hash");
        _ = ExpectString(runtime, "kubectlVersion", "v1.34.1", "kubectl-version");
    }

    private static void ValidateTopic(JsonElement topic)
    {
        RequireExactObject(topic, TopicProperties);
        _ = ExpectString(topic, "name", Cp6P09RuntimeProfile.ExpectedTopic, "topic");
        _ = ExpectString(topic, "eventType", ExpectedEventType, "event-type");
        ExpectInteger(topic, "partitions", 3, "topic");
        ExpectInteger(topic, "retentionMs", 3_600_000, "topic");
        ExpectInteger(topic, "maxMessageBytes", 1_048_576, "topic");
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

        if (!actual.SequenceEqual(ExpectedAcls))
        {
            Fail("acl-mismatch", "Evidence ACLs differ from the exact ordered Profile ACLs.");
        }
    }

    private static IReadOnlyList<Cp6P09EvidenceCheck> ValidateChecks(JsonElement checks)
    {
        if (checks.GetArrayLength() == 0)
        {
            Fail("required-checks", "Evidence must contain at least one check.");
        }

        var values = new List<Cp6P09EvidenceCheck>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checks.EnumerateArray())
        {
            RequireExactObject(check, CheckProperties);
            var id = RequirePattern(check, "id", IdentifierPattern, "invalid-check");
            if (!ids.Add(id))
            {
                Fail("duplicate-check", "Evidence check identifiers must be unique.");
            }

            var result = RequireString(check, "result");
            if (result is not ("Passed" or "Failed"))
            {
                Fail("invalid-check", "Evidence check result must be Passed or Failed.");
            }

            var summary = RequireString(check, "summary");
            ValidateSummary(summary);
            values.Add(new Cp6P09EvidenceCheck(id, result, summary));
        }

        return values;
    }

    private static void ValidateTrace(JsonElement trace)
    {
        RequireExactObject(trace, TraceProperties);
        _ = RequirePattern(trace, "eventId", IdentifierPattern, "trace");
        _ = ExpectString(trace, "eventType", ExpectedEventType, "event-type");
        _ = ExpectString(trace, "topic", Cp6P09RuntimeProfile.ExpectedTopic, "topic");
        _ = RequirePattern(trace, "partitionKey", IdentifierPattern, "trace");
        _ = RequirePattern(trace, "traceId", TracePattern, "trace");
        var publisherSpanId = RequirePattern(trace, "publisherSpanId", SpanPattern, "trace");
        var receiverSpanId = RequirePattern(trace, "receiverSpanId", SpanPattern, "trace");
        _ = RequirePattern(trace, "invocationTraceId", TracePattern, "trace");
        var invokerSpanId = RequirePattern(trace, "invokerSpanId", SpanPattern, "trace");
        var invokedSpanId = RequirePattern(trace, "invokedSpanId", SpanPattern, "trace");

        if (publisherSpanId == receiverSpanId || invokerSpanId == invokedSpanId)
        {
            Fail("trace-span", "Trace parent and child span identifiers must differ.");
        }
    }

    private static Cp6P09EvidenceTeardown ValidateTeardown(JsonElement teardown)
    {
        RequireExactObject(teardown, TeardownProperties);
        var commandExitCode = RequireInteger(teardown, "commandExitCode");
        var containerCount = RequireNonnegativeInteger(teardown, "containerCount");
        var networkCount = RequireNonnegativeInteger(teardown, "networkCount");
        var volumeCount = RequireNonnegativeInteger(teardown, "volumeCount");
        var imageCount = RequireNonnegativeInteger(teardown, "imageCount");
        var temporaryDirectoryRemoved = RequireBoolean(teardown, "temporaryDirectoryRemoved");
        return new Cp6P09EvidenceTeardown(
            commandExitCode,
            containerCount,
            networkCount,
            volumeCount,
            imageCount,
            temporaryDirectoryRemoved);
    }

    private static void RequireExactObject(JsonElement element, IReadOnlyCollection<string> expectedProperties)
    {
        RequireKind(element, JsonValueKind.Object);
        var allowed = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                Fail("unknown-property", $"Property '{property.Name}' is not allowed in this evidence object.");
            }
        }

        foreach (var propertyName in expectedProperties)
        {
            if (!element.TryGetProperty(propertyName, out _))
            {
                Fail("missing-property", $"Required evidence property '{propertyName}' is missing.");
            }
        }
    }

    private static JsonElement RequireProperty(JsonElement parent, string propertyName, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            Fail("missing-property", $"Required evidence property '{propertyName}' is missing.");
        }

        RequireKind(property, kind);
        return property;
    }

    private static string RequireString(JsonElement parent, string propertyName) =>
        RequireProperty(parent, propertyName, JsonValueKind.String).GetString()!;

    private static string ExpectString(
        JsonElement parent,
        string propertyName,
        string expected,
        string checkId)
    {
        var value = RequireString(parent, propertyName);
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            Fail(checkId, $"Evidence property '{propertyName}' does not have its approved value.");
        }

        return value;
    }

    private static string RequirePattern(
        JsonElement parent,
        string propertyName,
        Regex pattern,
        string checkId)
    {
        var value = RequireString(parent, propertyName);
        if (!pattern.IsMatch(value))
        {
            Fail(checkId, $"Evidence property '{propertyName}' has an invalid format.");
        }

        return value;
    }

    private static DateTimeOffset RequireUtc(JsonElement parent, string propertyName)
    {
        var value = RequireString(parent, propertyName);
        var timestamp = default(DateTimeOffset);
        if (!UtcPattern.IsMatch(value) ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp) ||
            timestamp.Offset != TimeSpan.Zero)
        {
            Fail("invalid-time", $"Evidence property '{propertyName}' must be an RFC 3339 UTC timestamp ending in Z.");
        }

        return timestamp;
    }

    private static int RequireInteger(JsonElement parent, string propertyName)
    {
        var value = RequireProperty(parent, propertyName, JsonValueKind.Number);
        if (!value.TryGetInt32(out var result))
        {
            Fail("wrong-type", $"Evidence property '{propertyName}' must be a 32-bit integer.");
        }

        return result;
    }

    private static int RequireNonnegativeInteger(JsonElement parent, string propertyName)
    {
        var value = RequireInteger(parent, propertyName);
        if (value < 0)
        {
            Fail("invalid-count", $"Evidence property '{propertyName}' cannot be negative.");
        }

        return value;
    }

    private static void ExpectInteger(JsonElement parent, string propertyName, int expected, string checkId)
    {
        if (RequireInteger(parent, propertyName) != expected)
        {
            Fail(checkId, $"Evidence property '{propertyName}' does not have its approved value.");
        }
    }

    private static bool RequireBoolean(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            Fail("missing-property", $"Required evidence property '{propertyName}' is missing.");
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            Fail("wrong-type", $"Evidence property '{propertyName}' must be a Boolean.");
        }

        return value.GetBoolean();
    }

    private static void RequireKind(JsonElement element, JsonValueKind expected)
    {
        if (element.ValueKind != expected)
        {
            Fail("wrong-type", $"Expected evidence JSON kind {expected} but found {element.ValueKind}.");
        }
    }

    private static Regex SafeRegex(string pattern) => new(
        pattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [DoesNotReturn]
    private static void Fail(string checkId, string message) => throw new Cp6P09ContractException(checkId, message);

    private readonly record struct ExpectedAcl(
        string Principal,
        string ResourceType,
        string ResourceName,
        string Operation);
}
