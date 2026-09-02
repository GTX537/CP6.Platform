using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

public static class Cp6ReleaseValidator
{
    private static readonly string[] SystemRepositories = ["CP6", "CP6.CRM", "CP6.Platform", "CP6.Portal"];
    private static readonly string[] PlatformPackageIds =
    [
        "CP6.Platform.Abstractions",
        "CP6.Platform.AspNetCore",
        "CP6.Platform.Contracts",
        "CP6.Platform.Deployment",
        "CP6.Platform.EntityFramework",
        "CP6.Platform.Messaging",
        "CP6.Platform.Release"
    ];
    private static readonly Regex ReleaseTag = new(
        "^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ObjectKey = new(
        "^objects/sha256/[0-9a-f]{2}/[0-9a-f]{64}/[a-z0-9][a-z0-9.-]{0,127}\\.json$",
        RegexOptions.CultureInvariant);

    public static Cp6ValidatedReleaseDocument ValidateSystemCandidate(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;

        var candidateKind = Cp6ReleaseJsonRules.RequireString(root, "candidateKind", "candidate-kind");
        if (!string.Equals(candidateKind, "System", StringComparison.Ordinal))
        {
            throw Error("candidate-kind", "System validation requires candidateKind=System.");
        }

        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "candidateKind", "deployable", "createdAtUtc", "repositories", "packages", "images", "compatibility", "evidence", "lineage");
        RequireSchema(root, Cp6ReleaseContractIds.SystemManifest);
        var deployable = Cp6ReleaseJsonRules.RequireBoolean(root, "deployable", "deployable");
        if (!deployable) throw Error("deployable", "System candidates must be deployable.");
        RequireUtc(root, "createdAtUtc");

        var repositories = ValidateRepositories(root.GetProperty("repositories"));
        if (!repositories.SequenceEqual(SystemRepositories, StringComparer.Ordinal))
        {
            throw Error("repository-set", "System candidate repositories are not the exact approved set.");
        }

        var packages = ValidatePackages(root.GetProperty("packages"), requireExactPlatformSet: false);
        ValidateEvidenceSubjects(root.GetProperty("images"));
        ValidateObjectReferenceArray(root.GetProperty("evidence"), requireNonEmpty: true);
        ValidateCompatibility(root.GetProperty("compatibility"));
        ValidateLineage(root.GetProperty("lineage"));
        return CreateResult(root, canonical, candidateKind, null, deployable, repositories, packages);
    }

    public static Cp6ValidatedReleaseDocument ValidateCandidateResult(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "releaseTag", "repositories", "systemManifest", "releaseGateResult", "validationWorkflow", "trustPolicyVersion", "evidencePolicyVersion");
        RequireSchema(root, Cp6ReleaseContractIds.CandidateResult);
        RequireReleaseTag(root, "releaseTag");
        var repositories = ValidateRepositories(root.GetProperty("repositories"));
        if (!repositories.SequenceEqual(SystemRepositories, StringComparer.Ordinal))
        {
            throw Error("repository-set", "Candidate result repositories are not the exact approved set.");
        }

        ValidateObjectReference(root.GetProperty("systemManifest"));
        ValidateObjectReference(root.GetProperty("releaseGateResult"));
        ValidateWorkflow(root.GetProperty("validationWorkflow"), requireSuccessConclusion: true);
        RequirePositiveVersion(root, "trustPolicyVersion");
        RequirePositiveVersion(root, "evidencePolicyVersion");
        return CreateResult(root, canonical, null, null, null, repositories, []);
    }

    public static Cp6ValidatedReleaseDocument ValidateCandidateLocator(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "releaseTag", "subjectKind", "subject", "trustPolicyVersion", "signerKeyId", "createdAtUtc");
        RequireSchema(root, Cp6ReleaseContractIds.CandidateLocator);
        RequireReleaseTag(root, "releaseTag");
        var subjectKind = Cp6ReleaseJsonRules.RequireString(root, "subjectKind", "subject-kind");
        if (subjectKind is not ("SystemCandidateResult" or "PlatformReleaseCandidate"))
        {
            throw Error("subject-kind", "Locator subject kind is not approved.");
        }

        var subjectMediaType = ValidateObjectReference(root.GetProperty("subject"));
        var expectedMediaType = subjectKind == "SystemCandidateResult"
            ? Cp6ReleaseMediaTypes.CandidateResult
            : Cp6ReleaseMediaTypes.PlatformReleaseCandidate;
        if (!string.Equals(subjectMediaType, expectedMediaType, StringComparison.Ordinal))
        {
            throw Error("subject-media-type", "Locator subject media type does not match its lane.");
        }

        RequirePositiveVersion(root, "trustPolicyVersion");
        var keyId = Cp6ReleaseJsonRules.RequireString(root, "signerKeyId", "signer-key-id");
        if (!keyId.StartsWith("sha256:", StringComparison.Ordinal)) throw Error("signer-key-id", "Signer key ID must be SHA-256 based.");
        Cp6ReleaseJsonRules.RequireSha256(keyId[7..], "signer-key-id");
        RequireUtc(root, "createdAtUtc");
        return CreateResult(root, canonical, null, subjectKind, null, [], []);
    }

    public static Cp6ValidatedReleaseDocument ValidatePlatformCandidate(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;

        var candidateKind = Cp6ReleaseJsonRules.RequireString(root, "candidateKind", "candidate-kind");
        if (!string.Equals(candidateKind, "PlatformReference", StringComparison.Ordinal))
        {
            throw Error("candidate-kind", "Platform validation requires candidateKind=PlatformReference.");
        }

        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "candidateKind", "deployable", "createdAtUtc", "platformSource", "packages", "buildProvenance", "images", "evidence", "crmConsumer", "publisher", "verifier", "releaseGateResult", "policyVersions");
        RequireSchema(root, Cp6ReleaseContractIds.PlatformCandidate);
        var deployable = Cp6ReleaseJsonRules.RequireBoolean(root, "deployable", "deployable");
        if (deployable) throw Error("deployable", "Platform reference candidates are not deployable.");
        RequireUtc(root, "createdAtUtc");
        ValidateEvidenceSubject(root.GetProperty("platformSource"));
        var packages = ValidatePackages(root.GetProperty("packages"), requireExactPlatformSet: true);
        ValidateObjectReference(root.GetProperty("buildProvenance"));
        ValidateEvidenceSubjects(root.GetProperty("images"));
        ValidateObjectReferenceArray(root.GetProperty("evidence"), requireNonEmpty: true);
        ValidateWorkflow(root.GetProperty("crmConsumer"), requireSuccessConclusion: false);
        ValidateWorkflow(root.GetProperty("publisher"), requireSuccessConclusion: false);
        ValidateWorkflow(root.GetProperty("verifier"), requireSuccessConclusion: false);
        ValidateObjectReference(root.GetProperty("releaseGateResult"));
        ValidatePolicyVersions(root.GetProperty("policyVersions"));
        return CreateResult(root, canonical, candidateKind, null, deployable, [], packages);
    }

    private static byte[] RequireCanonical(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = Cp6DeterministicJson.Canonicalize(utf8Json);
        if (!utf8Json.SequenceEqual(canonical))
        {
            throw Error("non-canonical-json", "Release contract input must already be canonical JSON.");
        }

        return canonical;
    }

    private static void RequireSchema(JsonElement root, string expected)
    {
        var actual = Cp6ReleaseJsonRules.RequireString(root, "$schemaId", "schema-id");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Error("schema-id", $"Expected schema ID '{expected}'.");
        }
    }

    private static IReadOnlyList<string> ValidateRepositories(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "repositories must be an array.");
        var result = value.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String) throw Error("property-kind", "Repository names must be strings.");
            return item.GetString()!;
        }).ToArray();
        Cp6ReleaseJsonRules.RequireOrdinalSet(result, "repository-set");
        return result;
    }

    private static IReadOnlyList<string> ValidatePackages(JsonElement value, bool requireExactPlatformSet)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "packages must be an array.");
        var ids = new List<string>();
        var versions = new HashSet<string>(StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in value.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(
                package,
                "packageId", "version", "sourceGitSha", "authorSignedPackageSha256", "publishedPackageSha256", "feedIdentity", "feedTransformation", "signerFingerprint", "timestampPolicy");
            var id = Cp6ReleaseJsonRules.RequireString(package, "packageId", "package-id");
            var version = Cp6ReleaseJsonRules.RequireString(package, "version", "package-version");
            var source = Cp6ReleaseJsonRules.RequireString(package, "sourceGitSha", "package-source");
            Cp6ReleaseJsonRules.RequireGitSha(source, "package-source");
            var authorSignedHash = Cp6ReleaseJsonRules.RequireString(package, "authorSignedPackageSha256", "package-hash");
            var publishedHash = Cp6ReleaseJsonRules.RequireString(package, "publishedPackageSha256", "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(authorSignedHash, "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(publishedHash, "package-hash");
            _ = Cp6ReleaseJsonRules.RequireString(package, "feedIdentity", "feed-identity");
            var transformation = Cp6ReleaseJsonRules.RequireString(package, "feedTransformation", "feed-transformation");
            if (transformation is not ("None" or "Documented" or "BytePreserving")) throw Error("feed-transformation", "Feed transformation is not approved.");
            Cp6ReleaseJsonRules.RequireSha256(Cp6ReleaseJsonRules.RequireString(package, "signerFingerprint", "signer-fingerprint"), "signer-fingerprint");
            var timestampPolicy = Cp6ReleaseJsonRules.RequireString(package, "timestampPolicy", "timestamp-policy");
            if (timestampPolicy is not ("Rfc3161Required" or "TestOnlyNone")) throw Error("timestamp-policy", "Timestamp policy is not approved.");
            if (transformation == "BytePreserving" && !string.Equals(authorSignedHash, publishedHash, StringComparison.Ordinal))
                throw Error("package-hash", "Byte-preserving feed publication requires equal package hashes.");
            if (transformation == "BytePreserving" && timestampPolicy != "Rfc3161Required")
                throw Error("timestamp-policy", "Byte-preserving formal packages require RFC3161 timestamps.");
            ids.Add(id);
            versions.Add(version);
            sources.Add(source);
        }

        Cp6ReleaseJsonRules.RequireOrdinalSet(ids, "package-set");
        if (versions.Count != 1) throw Error("package-version", "All packages must have one exact version.");
        if (sources.Count != 1) throw Error("package-source", "All packages must have one exact source SHA.");
        if (requireExactPlatformSet && !ids.SequenceEqual(PlatformPackageIds, StringComparer.Ordinal))
        {
            throw Error("package-set", "Platform candidate must contain the exact seven package IDs.");
        }

        if (!requireExactPlatformSet && ids.Count == 0) throw Error("package-set", "At least one package is required.");
        return ids;
    }

    private static string ValidateObjectReference(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "storageAuthority", "key", "mediaType", "sha256", "byteLength");
        var authority = Cp6ReleaseJsonRules.RequireString(value, "storageAuthority", "storage-authority");
        if (!string.Equals(authority, "cp6-release-r2-v1", StringComparison.Ordinal)) throw Error("storage-authority", "Storage authority is not approved.");
        var key = Cp6ReleaseJsonRules.RequireString(value, "key", "object-key");
        if (!ObjectKey.IsMatch(key)) throw Error("object-key", "Object key is not canonical.");
        var mediaType = Cp6ReleaseJsonRules.RequireString(value, "mediaType", "media-type");
        if (!Cp6ReleaseMediaTypes.All.Contains(mediaType, StringComparer.Ordinal)) throw Error("media-type", "Media type is not approved.");
        Cp6ReleaseJsonRules.RequireSha256(Cp6ReleaseJsonRules.RequireString(value, "sha256", "object-hash"), "object-hash");
        var byteLength = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "byteLength", "byte-length");
        if (byteLength is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("byte-length", "Object byte length is outside the approved range.");
        return mediaType;
    }

    private static void ValidateObjectReferenceArray(JsonElement value, bool requireNonEmpty)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "Expected an object-reference array.");
        var count = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateObjectReference(item);
            count++;
        }

        if (requireNonEmpty && count == 0) throw Error("evidence-required", "At least one object reference is required.");
    }

    private static void ValidateWorkflow(JsonElement value, bool requireSuccessConclusion)
    {
        var fields = requireSuccessConclusion
            ? new[] { "repository", "workflowPath", "workflowFileSha", "commitSha", "runId", "runAttempt", "environment", "conclusion" }
            : new[] { "repository", "workflowPath", "workflowFileSha", "commitSha", "runId", "runAttempt", "environment" };
        Cp6ReleaseJsonRules.RequireExactObject(value, fields);
        _ = Cp6ReleaseJsonRules.RequireString(value, "repository", "workflow-repository");
        _ = Cp6ReleaseJsonRules.RequireString(value, "workflowPath", "workflow-path");
        Cp6ReleaseJsonRules.RequireGitSha(Cp6ReleaseJsonRules.RequireString(value, "workflowFileSha", "workflow-file-sha"), "workflow-file-sha");
        Cp6ReleaseJsonRules.RequireGitSha(Cp6ReleaseJsonRules.RequireString(value, "commitSha", "workflow-commit-sha"), "workflow-commit-sha");
        if (Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "runId", "workflow-run-id") < 1) throw Error("workflow-run-id", "Workflow run ID must be positive.");
        if (Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "runAttempt", "workflow-run-attempt") < 1) throw Error("workflow-run-attempt", "Workflow run attempt must be positive.");
        _ = Cp6ReleaseJsonRules.RequireString(value, "environment", "workflow-environment");
        if (requireSuccessConclusion && !string.Equals(Cp6ReleaseJsonRules.RequireString(value, "conclusion", "workflow-conclusion"), "Success", StringComparison.Ordinal))
        {
            throw Error("workflow-conclusion", "Validation workflow must succeed.");
        }
    }

    private static void ValidateEvidenceSubjects(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "Expected an evidence-subject array.");
        foreach (var item in value.EnumerateArray()) ValidateEvidenceSubject(item);
    }

    private static void ValidateEvidenceSubject(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "subjectKind", "subjectName", "sha256OrDigest", "sourceGitSha");
        _ = Cp6ReleaseJsonRules.RequireString(value, "subjectKind", "subject-kind");
        _ = Cp6ReleaseJsonRules.RequireString(value, "subjectName", "subject-name");
        var hash = Cp6ReleaseJsonRules.RequireString(value, "sha256OrDigest", "subject-hash");
        if (hash.StartsWith("sha256:", StringComparison.Ordinal)) hash = hash[7..];
        Cp6ReleaseJsonRules.RequireSha256(hash, "subject-hash");
        Cp6ReleaseJsonRules.RequireGitSha(Cp6ReleaseJsonRules.RequireString(value, "sourceGitSha", "subject-source"), "subject-source");
    }

    private static void ValidateCompatibility(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "schemaVersion", "minimumConsumerVersion");
        if (Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "schemaVersion", "schema-version") < 1) throw Error("schema-version", "Schema version must be positive.");
        _ = Cp6ReleaseJsonRules.RequireString(value, "minimumConsumerVersion", "minimum-consumer-version");
    }

    private static void ValidateLineage(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "lineageMode", "predecessor");
        var mode = Cp6ReleaseJsonRules.RequireString(value, "lineageMode", "lineage-mode");
        if (mode is not ("Bootstrap" or "Successor")) throw Error("lineage-mode", "Lineage mode is not approved.");
        var predecessor = value.GetProperty("predecessor");
        if (mode == "Bootstrap" && predecessor.ValueKind != JsonValueKind.Null) throw Error("lineage-mode", "Bootstrap lineage cannot have a predecessor.");
        if (mode == "Successor") ValidateObjectReference(predecessor);
    }

    private static void ValidatePolicyVersions(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "trust", "evidence");
        RequirePositiveVersion(value, "trust");
        RequirePositiveVersion(value, "evidence");
    }

    private static void RequirePositiveVersion(JsonElement value, string name)
    {
        if (Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, name, "policy-version") < 1)
        {
            throw Error("policy-version", $"Policy version '{name}' must be positive.");
        }
    }

    private static void RequireReleaseTag(JsonElement value, string name)
    {
        var tag = Cp6ReleaseJsonRules.RequireString(value, name, "release-tag");
        if (!ReleaseTag.IsMatch(tag) || tag.Contains("..", StringComparison.Ordinal)) throw Error("release-tag", "Release tag is not canonical.");
    }

    private static void RequireUtc(JsonElement value, string name) =>
        Cp6ReleaseJsonRules.RequireUtcMilliseconds(Cp6ReleaseJsonRules.RequireString(value, name, "utc-timestamp"), "utc-timestamp");

    private static Cp6ValidatedReleaseDocument CreateResult(
        JsonElement root,
        byte[] canonical,
        string? candidateKind,
        string? subjectKind,
        bool? deployable,
        IReadOnlyList<string> repositories,
        IReadOnlyList<string> packages)
    {
        var hashes = new List<string>();
        CollectSubjectHashes(root, hashes);
        return new(
            root.GetProperty("$schemaId").GetString()!,
            candidateKind,
            subjectKind,
            deployable,
            Cp6DeterministicJson.Sha256Hex(canonical),
            repositories.ToArray(),
            packages.ToArray(),
            hashes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            canonical.ToArray());
    }

    private static void CollectSubjectHashes(JsonElement value, List<string> hashes)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Name is "sha256" or "sha256OrDigest" or "authorSignedPackageSha256" or "publishedPackageSha256")
                {
                    hashes.Add(property.Value.GetString()!);
                }

                CollectSubjectHashes(property.Value, hashes);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) CollectSubjectHashes(item, hashes);
        }
    }

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);
}
