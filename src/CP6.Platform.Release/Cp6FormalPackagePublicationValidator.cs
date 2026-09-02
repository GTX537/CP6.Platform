using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

public static class Cp6FormalPackagePublicationValidator
{
    private const string FormalVersion = "0.10.0";
    private const string FeedPrefix = "https://nuget.pkg.github.com/GTX537/index.json#";
    private static readonly Regex BuildInvocation = new(
        "^p10-s04:(?<sha>[0-9a-f]{40}):(?<run>[1-9][0-9]*):(?<attempt>[1-9][0-9]*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex PolicyOid = new(
        "^[0-2](?:\\.(?:0|[1-9][0-9]*))+$",
        RegexOptions.CultureInvariant);

    public static Cp6ValidatedReleaseDocument ValidateFormalPackagePublication(
        ReadOnlySpan<byte> utf8Json,
        Cp6PinnedNuGetTrustPolicy trustPolicy,
        DateTimeOffset evaluationUtc)
    {
        ArgumentNullException.ThrowIfNull(trustPolicy);
        var canonical = Cp6DeterministicJson.Canonicalize(utf8Json);
        if (!utf8Json.SequenceEqual(canonical))
        {
            throw Error("non-canonical-json", "Formal publication must already be canonical JSON.");
        }

        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "createdAtUtc", "version", "sourceGitSha", "buildInvocationId",
            "workflow", "toolchain", "trust", "packages", "verification");
        RequireExact(root, "$schemaId", Cp6ReleaseContractIds.FormalPackagePublication, "schema-id");
        var createdAtUtc = ParseUtc(root, "createdAtUtc");
        RequireExact(root, "version", FormalVersion, "package-version");
        var sourceGitSha = Cp6ReleaseJsonRules.RequireString(root, "sourceGitSha", "package-source");
        Cp6ReleaseJsonRules.RequireGitSha(sourceGitSha, "package-source");

        var invocation = ParseBuildInvocation(root, sourceGitSha);
        ValidateWorkflow(root.GetProperty("workflow"), sourceGitSha, invocation);
        ValidateToolchain(root.GetProperty("toolchain"));
        ValidateTrust(root.GetProperty("trust"), trustPolicy, createdAtUtc, evaluationUtc);
        var (packageIds, hashes) = ValidatePackages(root.GetProperty("packages"), sourceGitSha, trustPolicy);
        ValidateVerification(root.GetProperty("verification"));

        return new(
            Cp6ReleaseContractIds.FormalPackagePublication,
            null,
            null,
            null,
            Cp6DeterministicJson.Sha256Hex(canonical),
            [],
            packageIds,
            hashes,
            canonical.ToArray());
    }

    private static BuildIdentity ParseBuildInvocation(JsonElement root, string sourceGitSha)
    {
        var value = Cp6ReleaseJsonRules.RequireString(root, "buildInvocationId", "build-invocation");
        var match = BuildInvocation.Match(value);
        if (!match.Success ||
            !string.Equals(match.Groups["sha"].Value, sourceGitSha, StringComparison.Ordinal) ||
            !long.TryParse(match.Groups["run"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var runId) ||
            !long.TryParse(match.Groups["attempt"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var runAttempt))
        {
            throw Error("build-invocation", "Build invocation does not bind a valid source and workflow run.");
        }

        return new(runId, runAttempt);
    }

    private static void ValidateWorkflow(JsonElement value, string sourceGitSha, BuildIdentity invocation)
    {
        Cp6ReleaseJsonRules.RequireExactObject(
            value,
            "repository", "workflowPath", "workflowFileSha", "commitSha", "runId", "runAttempt", "environment");
        RequireExact(value, "repository", "GTX537/CP6.Platform", "workflow-repository");
        RequireExact(value, "workflowPath", ".github/workflows/p10-formal-packages.yml", "workflow-path");
        RequireExact(value, "environment", "p10-formal-release", "workflow-environment");
        Cp6ReleaseJsonRules.RequireGitSha(
            Cp6ReleaseJsonRules.RequireString(value, "workflowFileSha", "workflow-file-sha"),
            "workflow-file-sha");
        var commit = Cp6ReleaseJsonRules.RequireString(value, "commitSha", "workflow-commit-sha");
        Cp6ReleaseJsonRules.RequireGitSha(commit, "workflow-commit-sha");
        var runId = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "runId", "workflow-run-id");
        var runAttempt = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "runAttempt", "workflow-run-attempt");
        if (!string.Equals(commit, sourceGitSha, StringComparison.Ordinal) ||
            runId != invocation.RunId || runAttempt != invocation.RunAttempt)
        {
            throw Error("build-invocation", "Workflow identity does not match the source-bound build invocation.");
        }
    }

    private static void ValidateToolchain(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "dotnetSdk", "nugetClient", "runnerImage");
        _ = Cp6ReleaseJsonRules.RequireString(value, "dotnetSdk", "toolchain");
        _ = Cp6ReleaseJsonRules.RequireString(value, "nugetClient", "toolchain");
        _ = Cp6ReleaseJsonRules.RequireString(value, "runnerImage", "toolchain");
    }

    private static void ValidateTrust(
        JsonElement value,
        Cp6PinnedNuGetTrustPolicy trustPolicy,
        DateTimeOffset createdAtUtc,
        DateTimeOffset evaluationUtc)
    {
        Cp6ReleaseJsonRules.RequireExactObject(
            value,
            "policyVersion", "policySha256", "trustModel", "publicCaTrusted", "internallyTrusted",
            "signerFingerprint", "spkiKeyId", "timestampPolicy", "timestampService");
        var version = Cp6ReleaseJsonRules.RequireNonNegativeInteger(value, "policyVersion", "trust-policy");
        var policyHash = Cp6ReleaseJsonRules.RequireString(value, "policySha256", "trust-policy");
        Cp6ReleaseJsonRules.RequireSha256(policyHash, "trust-policy");
        if (version != trustPolicy.PolicyVersion ||
            !string.Equals(policyHash, trustPolicy.ValidatedDocument.Sha256, StringComparison.Ordinal))
        {
            throw Error("trust-policy", "Publication does not bind the supplied trust policy.");
        }

        RequireExact(value, "trustModel", trustPolicy.TrustModel, "trust-claim");
        if (Cp6ReleaseJsonRules.RequireBoolean(value, "publicCaTrusted", "trust-claim") != trustPolicy.PublicCaTrusted ||
            Cp6ReleaseJsonRules.RequireBoolean(value, "internallyTrusted", "trust-claim") != trustPolicy.InternallyTrusted)
        {
            throw Error("trust-claim", "Publication trust claims do not match the pinned policy.");
        }

        RequireExact(value, "timestampPolicy", trustPolicy.TimestampPolicy, "timestamp-policy");
        RequireExact(value, "timestampService", trustPolicy.TimestampService.OriginalString, "timestamp-policy");
        var fingerprint = Cp6ReleaseJsonRules.RequireString(value, "signerFingerprint", "trust-signer");
        Cp6ReleaseJsonRules.RequireSha256(fingerprint, "trust-signer");
        var spkiKeyId = Cp6ReleaseJsonRules.RequireString(value, "spkiKeyId", "trust-signer");
        if (!string.Equals(fingerprint, trustPolicy.CurrentSigner.CertificateSha256, StringComparison.Ordinal) ||
            !string.Equals(spkiKeyId, trustPolicy.CurrentSigner.SpkiKeyId, StringComparison.Ordinal))
        {
            throw Error("trust-signer", "Publication signer does not match the Current pinned signer.");
        }

        _ = trustPolicy.RequireSigner(
            fingerprint,
            createdAtUtc,
            evaluationUtc,
            Cp6ReleaseValidationMode.Current);
    }

    private static (IReadOnlyList<string> PackageIds, IReadOnlyList<string> Hashes) ValidatePackages(
        JsonElement value,
        string sourceGitSha,
        Cp6PinnedNuGetTrustPolicy trustPolicy)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Error("property-kind", "packages must be an array.");
        }

        var ids = new List<string>();
        var hashes = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(
                item,
                "packageId", "version", "sourceGitSha", "authorSignedPackageSha256", "publishedPackageSha256",
                "feedIdentity", "feedTransformation", "signerFingerprint", "timestampPolicy", "timestampPolicyOid",
                "timestampCertificateChainSha256");
            var id = Cp6ReleaseJsonRules.RequireString(item, "packageId", "package-id");
            RequireExact(item, "version", FormalVersion, "package-version");
            RequireExact(item, "sourceGitSha", sourceGitSha, "package-source");
            var signedHash = Cp6ReleaseJsonRules.RequireString(item, "authorSignedPackageSha256", "package-hash");
            var publishedHash = Cp6ReleaseJsonRules.RequireString(item, "publishedPackageSha256", "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(signedHash, "package-hash");
            Cp6ReleaseJsonRules.RequireSha256(publishedHash, "package-hash");
            RequireExact(item, "feedTransformation", "BytePreserving", "feed-transformation");
            if (!string.Equals(signedHash, publishedHash, StringComparison.Ordinal))
            {
                throw Error("package-hash", "Byte-preserving publication requires identical signed and published hashes.");
            }

            RequireExact(item, "feedIdentity", $"{FeedPrefix}{id}/{FormalVersion}", "feed-identity");
            RequireExact(item, "signerFingerprint", trustPolicy.CurrentSigner.CertificateSha256, "trust-signer");
            RequireExact(item, "timestampPolicy", "Rfc3161Required", "timestamp-policy");
            var oid = Cp6ReleaseJsonRules.RequireString(item, "timestampPolicyOid", "timestamp-policy");
            if (!PolicyOid.IsMatch(oid))
            {
                throw Error("timestamp-policy", "Timestamp policy OID is not canonical.");
            }

            ValidateTimestampChain(item.GetProperty("timestampCertificateChainSha256"));
            ids.Add(id);
            hashes.Add(signedHash);
        }

        Cp6ReleaseJsonRules.RequireOrdinalSet(ids, "package-set");
        if (!ids.SequenceEqual(trustPolicy.AllowedPackageIds, StringComparer.Ordinal))
        {
            throw Error("package-set", "Publication must contain the exact seven pinned package IDs.");
        }

        var distinctHashes = hashes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (distinctHashes.Length != ids.Count)
        {
            throw Error("package-hash", "Each formal package must have a distinct subject hash.");
        }

        return (ids.ToArray(), distinctHashes);
    }

    private static void ValidateTimestampChain(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Error("property-kind", "Timestamp certificate chain must be an array.");
        }

        var hashes = value.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Error("property-kind", "Timestamp certificate hashes must be strings.");
            }

            var hash = item.GetString()!;
            Cp6ReleaseJsonRules.RequireSha256(hash, "timestamp-chain");
            return hash;
        }).ToArray();
        if (hashes.Length == 0 || hashes.Distinct(StringComparer.Ordinal).Count() != hashes.Length)
        {
            throw Error("timestamp-chain", "Timestamp certificate chain must be non-empty and unique.");
        }
    }

    private static void ValidateVerification(JsonElement value)
    {
        Cp6ReleaseJsonRules.RequireExactObject(value, "windows", "linux");
        RequireExact(value, "windows", "Success", "verification");
        RequireExact(value, "linux", "Success", "verification");
    }

    private static DateTimeOffset ParseUtc(JsonElement value, string name)
    {
        var text = Cp6ReleaseJsonRules.RequireString(value, name, "utc-timestamp");
        Cp6ReleaseJsonRules.RequireUtcMilliseconds(text, "utc-timestamp");
        return DateTimeOffset.ParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static void RequireExact(JsonElement value, string name, string expected, string code)
    {
        if (!string.Equals(Cp6ReleaseJsonRules.RequireString(value, name, code), expected, StringComparison.Ordinal))
        {
            throw Error(code, $"Property '{name}' does not match its required value.");
        }
    }

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);

    private sealed record BuildIdentity(long RunId, long RunAttempt);
}
