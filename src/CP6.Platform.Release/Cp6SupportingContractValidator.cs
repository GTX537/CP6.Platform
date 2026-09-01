using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

public static class Cp6SupportingContractValidator
{
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
    private static readonly Regex ObjectKey = new(
        "^objects/sha256/(?<prefix>[0-9a-f]{2})/(?<hash>[0-9a-f]{64})/[a-z0-9][a-z0-9.-]{0,127}\\.json$",
        RegexOptions.CultureInvariant);
    private static readonly Regex BuildInvocation = new(
        "^p10-s02:(?<sha>[0-9a-f]{40}):[1-9][0-9]*:[1-9][0-9]*$",
        RegexOptions.CultureInvariant);

    public static Cp6ValidatedReleaseDocument ValidateReleaseGateResult(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(root, "$schemaId", "createdAtUtc", "workflow", "inputSubjects", "gates", "conclusion");
        RequireSchema(root, Cp6ReleaseContractIds.ReleaseGateResult);
        RequireUtc(root, "createdAtUtc");
        ValidateWorkflow(root.GetProperty("workflow"));
        var inputHashes = ValidateEvidenceSubjectArray(root.GetProperty("inputSubjects"), requireNonEmpty: true).Select(subject => subject.Hash).ToHashSet(StringComparer.Ordinal);
        var gates = Cp6ReleaseJsonRules.RequireProperty(root, "gates", JsonValueKind.Array);
        var gateNames = new List<string>();
        foreach (var gate in gates.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(gate, "name", "subjectHash", "conclusion");
            gateNames.Add(Cp6ReleaseJsonRules.RequireString(gate, "name", "gate-name"));
            var subjectHash = Cp6ReleaseJsonRules.RequireString(gate, "subjectHash", "gate-subject-binding");
            Cp6ReleaseJsonRules.RequireSha256(subjectHash, "gate-subject-binding");
            if (!inputHashes.Contains(subjectHash)) throw Error("gate-subject-binding", "Gate subject is not an input subject.");
            RequireConclusion(gate, "conclusion");
        }
        Cp6ReleaseJsonRules.RequireOrdinalSet(gateNames, "gate-set");
        if (gateNames.Count == 0) throw Error("gate-set", "At least one gate is required.");
        RequireConclusion(root, "conclusion");
        return Create(root, canonical, [], inputHashes.Order(StringComparer.Ordinal).ToArray());
    }

    public static Cp6ValidatedReleaseDocument ValidateSystemLineageBootstrapEvidence(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(root, "$schemaId", "createdAtUtc", "authority", "systemManifestSubject", "reason", "trustPolicyVersion", "signaturePolicy");
        RequireSchema(root, Cp6ReleaseContractIds.SystemLineageBootstrap);
        RequireUtc(root, "createdAtUtc");
        ValidateWorkflow(root.GetProperty("authority"));
        var subject = ValidateEvidenceSubject(root.GetProperty("systemManifestSubject"));
        _ = Cp6ReleaseJsonRules.RequireString(root, "reason", "bootstrap-reason");
        RequirePositiveVersion(root, "trustPolicyVersion");
        if (!string.Equals(Cp6ReleaseJsonRules.RequireString(root, "signaturePolicy", "signature-policy"), "BootstrapOnly", StringComparison.Ordinal))
            throw Error("signature-policy", "Bootstrap evidence requires BootstrapOnly signature policy.");
        return Create(root, canonical, [], [subject.Hash]);
    }

    public static Cp6ValidatedReleaseDocument ValidateEvidenceRecord(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(root, "$schemaId", "createdAtUtc", "evidenceKind", "producer", "policyVersion", "accessClass", "object", "subjects", "conclusion");
        RequireSchema(root, Cp6ReleaseContractIds.EvidenceRecord);
        RequireUtc(root, "createdAtUtc");
        _ = Cp6ReleaseJsonRules.RequireString(root, "evidenceKind", "evidence-kind");
        ValidateWorkflow(root.GetProperty("producer"));
        RequirePositiveVersion(root, "policyVersion");
        var accessClass = Cp6ReleaseJsonRules.RequireString(root, "accessClass", "access-class");
        if (accessClass is not ("RequiredPublic" or "RestrictedAudit" or "TestOnly")) throw Error("access-class", "Evidence access class is not approved.");
        var objectReference = ValidateObjectReference(root.GetProperty("object"));
        var subjects = ValidateEvidenceSubjectArray(root.GetProperty("subjects"), requireNonEmpty: true);
        if (!subjects.Any(subject => string.Equals(subject.Hash, objectReference.Hash, StringComparison.Ordinal)))
            throw Error("evidence-binding", "Evidence object is not bound to a declared subject.");
        RequireConclusion(root, "conclusion");
        return Create(root, canonical, [], subjects.Select(subject => subject.Hash).ToArray(), accessClass);
    }

    public static Cp6ValidatedReleaseDocument ValidateBuildInvocationProvenance(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(root, "$schemaId", "createdAtUtc", "sourceGitSha", "buildInvocationId", "toolchain", "preSignOutputs", "finalPackages");
        RequireSchema(root, Cp6ReleaseContractIds.BuildProvenance);
        RequireUtc(root, "createdAtUtc");
        var source = Cp6ReleaseJsonRules.RequireString(root, "sourceGitSha", "build-source");
        Cp6ReleaseJsonRules.RequireGitSha(source, "build-source");
        var invocation = Cp6ReleaseJsonRules.RequireString(root, "buildInvocationId", "build-invocation");
        var invocationMatch = BuildInvocation.Match(invocation);
        if (!invocationMatch.Success || !string.Equals(invocationMatch.Groups["sha"].Value, source, StringComparison.Ordinal))
            throw Error("build-invocation", "Build invocation does not bind the source SHA.");
        ValidateToolchain(root.GetProperty("toolchain"));

        var preSign = ParsePreSignOutputs(root.GetProperty("preSignOutputs"));
        var final = ParseFinalPackages(root.GetProperty("finalPackages"), source);
        if (!preSign.Keys.SequenceEqual(PlatformPackageIds, StringComparer.Ordinal) || !final.Keys.SequenceEqual(PlatformPackageIds, StringComparer.Ordinal))
            throw Error("package-set", "Build provenance must map the exact seven packages.");
        foreach (var id in PlatformPackageIds)
        {
            if (!string.Equals(preSign[id], final[id].PreSignHash, StringComparison.Ordinal))
                throw Error("provenance-mapping", "Final package does not map to its pre-sign output.");
        }
        return Create(root, canonical, PlatformPackageIds, final.Values.Select(value => value.FinalHash).ToArray());
    }

    public static Cp6ValidatedReleaseDocument ValidateTestPackageTransport(ReadOnlySpan<byte> utf8Json, DateTimeOffset evaluationUtc)
    {
        var canonical = RequireCanonical(utf8Json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(root, "$schemaId", "testOnly", "platformSourceSha", "workflow", "packageArtifact", "createdAtUtc", "expiresAtUtc");
        RequireSchema(root, Cp6ReleaseContractIds.TestPackageTransport);
        if (!Cp6ReleaseJsonRules.RequireBoolean(root, "testOnly", "test-only")) throw Error("test-only", "Transport must be test-only.");
        var source = Cp6ReleaseJsonRules.RequireString(root, "platformSourceSha", "transport-source");
        Cp6ReleaseJsonRules.RequireGitSha(source, "transport-source");
        var workflow = ValidateWorkflow(root.GetProperty("workflow"));
        if (!string.Equals(workflow.CommitSha, source, StringComparison.Ordinal)) throw Error("transport-binding", "Workflow commit does not match transport source.");
        var artifact = root.GetProperty("packageArtifact");
        Cp6ReleaseJsonRules.RequireExactObject(artifact, "artifactId", "digest", "sourceRunId", "sourceRunAttempt");
        if (Cp6ReleaseJsonRules.RequireNonNegativeInteger(artifact, "artifactId", "artifact-id") < 1) throw Error("artifact-id", "Artifact ID must be positive.");
        var digest = Cp6ReleaseJsonRules.RequireString(artifact, "digest", "artifact-digest");
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) throw Error("artifact-digest", "Artifact digest must be SHA-256.");
        Cp6ReleaseJsonRules.RequireSha256(digest[7..], "artifact-digest");
        var sourceRunId = Cp6ReleaseJsonRules.RequireNonNegativeInteger(artifact, "sourceRunId", "transport-binding");
        var sourceRunAttempt = Cp6ReleaseJsonRules.RequireNonNegativeInteger(artifact, "sourceRunAttempt", "transport-binding");
        if (sourceRunId != workflow.RunId || sourceRunAttempt != workflow.RunAttempt) throw Error("transport-binding", "Artifact run identity does not match workflow identity.");
        var created = ParseUtc(root, "createdAtUtc");
        var expires = ParseUtc(root, "expiresAtUtc");
        if (expires <= created) throw Error("transport-expiry", "Transport expiry must follow creation.");
        if (evaluationUtc >= expires) throw Error("transport-expired", "Test package transport has expired.");
        return Create(root, canonical, [], [digest]);
    }

    public static void RequireSystemLineage(ReadOnlySpan<byte> systemManifestUtf8, ReadOnlySpan<byte> bootstrapEvidenceUtf8)
    {
        var system = Cp6ReleaseValidator.ValidateSystemCandidate(systemManifestUtf8);
        using var systemDocument = JsonDocument.Parse(system.CanonicalUtf8);
        var mode = systemDocument.RootElement.GetProperty("lineage").GetProperty("lineageMode").GetString();
        if (string.Equals(mode, "Bootstrap", StringComparison.Ordinal))
        {
            if (bootstrapEvidenceUtf8.IsEmpty) throw Error("bootstrap-required", "Bootstrap lineage requires bootstrap evidence.");
            var bootstrap = ValidateSystemLineageBootstrapEvidence(bootstrapEvidenceUtf8);
            if (!bootstrap.SubjectHashes.Contains(system.Sha256, StringComparer.Ordinal)) throw Error("bootstrap-binding", "Bootstrap evidence does not bind the System manifest.");
            return;
        }
        if (!bootstrapEvidenceUtf8.IsEmpty) throw Error("bootstrap-forbidden", "Successor lineage forbids bootstrap evidence.");
    }

    public static void RequireRequiredPublicEvidence(
        IReadOnlyList<Cp6ValidatedReleaseDocument> evidence,
        IReadOnlyList<string> acceptedSubjectHashes)
    {
        var covered = evidence
            .Where(item => string.Equals(item.SubjectKind, "RequiredPublic", StringComparison.Ordinal))
            .SelectMany(item => item.SubjectHashes)
            .ToHashSet(StringComparer.Ordinal);
        if (acceptedSubjectHashes.Any(hash => !covered.Contains(hash)))
            throw Error("required-public-evidence", "RequiredPublic evidence does not cover every accepted subject.");
    }

    public static void RequireCandidateLocatorSubjectBinding(ReadOnlySpan<byte> locatorUtf8, ReadOnlySpan<byte> subjectUtf8)
    {
        var locator = Cp6ReleaseValidator.ValidateCandidateLocator(locatorUtf8);
        Cp6ValidatedReleaseDocument subject;
        if (string.Equals(locator.SubjectKind, "PlatformReleaseCandidate", StringComparison.Ordinal))
            subject = Cp6ReleaseValidator.ValidatePlatformCandidate(subjectUtf8);
        else
            subject = Cp6ReleaseValidator.ValidateCandidateResult(subjectUtf8);
        using var locatorDocument = JsonDocument.Parse(locator.CanonicalUtf8);
        using var subjectDocument = JsonDocument.Parse(subject.CanonicalUtf8);
        var locatorCreated = locatorDocument.RootElement.GetProperty("createdAtUtc").GetString();
        var subjectCreated = subjectDocument.RootElement.GetProperty("createdAtUtc").GetString();
        if (!string.Equals(locatorCreated, subjectCreated, StringComparison.Ordinal))
            throw Error("locator-created-at", "Locator creation time must equal subject creation time.");
    }

    private static byte[] RequireCanonical(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = Cp6DeterministicJson.Canonicalize(utf8Json);
        if (!utf8Json.SequenceEqual(canonical)) throw Error("non-canonical-json", "Supporting contract must already be canonical JSON.");
        return canonical;
    }

    private static void RequireSchema(JsonElement root, string expected)
    {
        var actual = Cp6ReleaseJsonRules.RequireString(root, "$schemaId", "schema-id");
        if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw Error("schema-id", "Supporting contract schema ID is invalid.");
    }

    private static ObjectReference ValidateObjectReference(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "storageAuthority", "key", "mediaType", "sha256", "byteLength");
        if (!string.Equals(Cp6ReleaseJsonRules.RequireString(value, "storageAuthority", "storage-authority"), "cp6-release-r2-v1", StringComparison.Ordinal))
            throw Error("storage-authority", "Object reference storage authority is not pinned.");
        var key = Cp6ReleaseJsonRules.RequireString(value, "key", "object-key");
        var keyMatch = ObjectKey.Match(key);
        if (!keyMatch.Success) throw Error("object-key", "Object key is not canonical.");
        var mediaType = Cp6ReleaseJsonRules.RequireString(value, "mediaType", "media-type");
        if (!Cp6ReleaseMediaTypes.All.Contains(mediaType, StringComparer.Ordinal)) throw Error("media-type", "Object media type is not approved.");
        var hash = Cp6ReleaseJsonRules.RequireString(value, "sha256", "object-hash");
        Cp6ReleaseJsonRules.RequireSha256(hash, "object-hash");
        if (!string.Equals(keyMatch.Groups["prefix"].Value, hash[..2], StringComparison.Ordinal) ||
            !string.Equals(keyMatch.Groups["hash"].Value, hash, StringComparison.Ordinal))
            throw Error("object-key-binding", "Object key does not bind its hash.");
        var length = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "byteLength", "byte-length");
        if (length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("byte-length", "Object byte length is outside the approved range.");
        return new(hash, mediaType);
    }

    private static WorkflowIdentity ValidateWorkflow(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "repository", "workflowPath", "workflowFileSha", "commitSha", "runId", "runAttempt", "environment");
        var repository = Cp6ReleaseJsonRules.RequireString(value, "repository", "workflow-repository");
        _ = Cp6ReleaseJsonRules.RequireString(value, "workflowPath", "workflow-path");
        Cp6ReleaseJsonRules.RequireGitSha(Cp6ReleaseJsonRules.RequireString(value, "workflowFileSha", "workflow-file-sha"), "workflow-file-sha");
        var commit = Cp6ReleaseJsonRules.RequireString(value, "commitSha", "workflow-commit-sha");
        Cp6ReleaseJsonRules.RequireGitSha(commit, "workflow-commit-sha");
        var runId = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "runId", "workflow-run-id");
        var attempt = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "runAttempt", "workflow-run-attempt");
        if (runId < 1 || attempt < 1) throw Error("workflow-run-id", "Workflow run identity must be positive.");
        _ = Cp6ReleaseJsonRules.RequireString(value, "environment", "workflow-environment");
        return new(repository, commit, runId, attempt);
    }

    private static IReadOnlyList<EvidenceSubject> ValidateEvidenceSubjectArray(JsonElement value, bool requireNonEmpty)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "Evidence subjects must be an array.");
        var result = value.EnumerateArray().Select(ValidateEvidenceSubject).ToArray();
        if (requireNonEmpty && result.Length == 0) throw Error("evidence-subject", "At least one evidence subject is required.");
        var identities = result.Select(subject => $"{subject.Kind}\0{subject.Name}\0{subject.Hash}").ToArray();
        Cp6ReleaseJsonRules.RequireOrdinalSet(identities, "evidence-subject");
        return result;
    }

    private static EvidenceSubject ValidateEvidenceSubject(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "subjectKind", "subjectName", "sha256OrDigest", "sourceGitSha");
        var kind = Cp6ReleaseJsonRules.RequireString(value, "subjectKind", "subject-kind");
        var name = Cp6ReleaseJsonRules.RequireString(value, "subjectName", "subject-name");
        var hash = Cp6ReleaseJsonRules.RequireString(value, "sha256OrDigest", "subject-hash");
        if (hash.StartsWith("sha256:", StringComparison.Ordinal)) hash = hash[7..];
        Cp6ReleaseJsonRules.RequireSha256(hash, "subject-hash");
        var source = Cp6ReleaseJsonRules.RequireString(value, "sourceGitSha", "subject-source");
        Cp6ReleaseJsonRules.RequireGitSha(source, "subject-source");
        return new(kind, name, hash, source);
    }

    private static IReadOnlyDictionary<string, string> ParsePreSignOutputs(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "preSignOutputs must be an array.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(item, "packageId", "sha256");
            var id = Cp6ReleaseJsonRules.RequireString(item, "packageId", "package-id");
            var hash = Cp6ReleaseJsonRules.RequireString(item, "sha256", "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(hash, "package-hash");
            if (!result.TryAdd(id, hash)) throw Error("package-set", "Duplicate pre-sign package.");
        }
        RequireOrderedKeys(result, "package-set");
        return result;
    }

    private static IReadOnlyDictionary<string, FinalPackage> ParseFinalPackages(JsonElement value, string source)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("property-kind", "finalPackages must be an array.");
        var result = new Dictionary<string, FinalPackage>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(item, "packageId", "preSignSha256", "finalSha256", "subject");
            var id = Cp6ReleaseJsonRules.RequireString(item, "packageId", "package-id");
            var preSignHash = Cp6ReleaseJsonRules.RequireString(item, "preSignSha256", "package-hash");
            var finalHash = Cp6ReleaseJsonRules.RequireString(item, "finalSha256", "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(preSignHash, "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(finalHash, "package-hash");
            var subject = ValidateEvidenceSubject(item.GetProperty("subject"));
            if (!string.Equals(subject.Name, id, StringComparison.Ordinal) ||
                !string.Equals(subject.Hash, finalHash, StringComparison.Ordinal) ||
                !string.Equals(subject.Source, source, StringComparison.Ordinal))
                throw Error("provenance-mapping", "Final package subject is not exactly bound.");
            if (!result.TryAdd(id, new(preSignHash, finalHash))) throw Error("package-set", "Duplicate final package.");
        }
        RequireOrderedKeys(result, "package-set");
        return result;
    }

    private static void RequireOrderedKeys<T>(IReadOnlyDictionary<string, T> values, string code)
    {
        if (!values.Keys.SequenceEqual(values.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Error(code, "Package IDs must be ordinal-sorted.");
    }

    private static void ValidateToolchain(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "dotnetSdk", "runner");
        _ = Cp6ReleaseJsonRules.RequireString(value, "dotnetSdk", "toolchain");
        _ = Cp6ReleaseJsonRules.RequireString(value, "runner", "toolchain");
    }

    private static void RequireConclusion(JsonElement value, string name)
    {
        var conclusion = Cp6ReleaseJsonRules.RequireString(value, name, "conclusion");
        if (conclusion is not ("Success" or "Failure")) throw Error("conclusion", "Conclusion is not approved.");
    }

    private static int RequirePositiveVersion(JsonElement value, string name)
    {
        var result = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, name, "policy-version");
        if (result is < 1 or > int.MaxValue) throw Error("policy-version", "Policy version must be a positive Int32.");
        return (int)result;
    }

    private static DateTimeOffset ParseUtc(JsonElement value, string name)
    {
        var text = Cp6ReleaseJsonRules.RequireString(value, name, "utc-timestamp");
        Cp6ReleaseJsonRules.RequireUtcMilliseconds(text, "utc-timestamp");
        return DateTimeOffset.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static void RequireUtc(JsonElement value, string name) => _ = ParseUtc(value, name);

    private static Cp6ValidatedReleaseDocument Create(
        JsonElement root,
        byte[] canonical,
        IReadOnlyList<string> packageIds,
        IReadOnlyList<string> subjectHashes,
        string? subjectKind = null) =>
        new(
            root.GetProperty("$schemaId").GetString()!,
            null,
            subjectKind,
            null,
            Cp6DeterministicJson.Sha256Hex(canonical),
            [],
            packageIds.ToArray(),
            subjectHashes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            canonical.ToArray());

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);

    private sealed record ObjectReference(string Hash, string MediaType);
    private sealed record WorkflowIdentity(string Repository, string CommitSha, long RunId, long RunAttempt);
    private sealed record EvidenceSubject(string Kind, string Name, string Hash, string Source);
    private sealed record FinalPackage(string PreSignHash, string FinalHash);
}
