using System.Globalization;
using System.Text.Json;

namespace CP6.Platform.Release;

public sealed class Cp6PinnedTrustPolicy
{
    private readonly IReadOnlyDictionary<string, Cp6PinnedTrustKey> _keys;

    private Cp6PinnedTrustPolicy(
        int policyVersion,
        int minimumAcceptedPolicyVersion,
        IReadOnlySet<int> acceptedHistoricalPolicyVersions,
        IReadOnlyDictionary<string, Cp6PinnedTrustKey> keys,
        Cp6ValidatedReleaseDocument validatedDocument)
    {
        PolicyVersion = policyVersion;
        MinimumAcceptedPolicyVersion = minimumAcceptedPolicyVersion;
        AcceptedHistoricalPolicyVersions = acceptedHistoricalPolicyVersions;
        _keys = keys;
        ValidatedDocument = validatedDocument;
    }

    public int PolicyVersion { get; }
    public int MinimumAcceptedPolicyVersion { get; }
    public IReadOnlySet<int> AcceptedHistoricalPolicyVersions { get; }
    public Cp6ValidatedReleaseDocument ValidatedDocument { get; }

    public static Cp6PinnedTrustPolicy Parse(ReadOnlySpan<byte> utf8Json)
    {
        var canonical = Cp6DeterministicJson.Canonicalize(utf8Json);
        if (!utf8Json.SequenceEqual(canonical)) throw Error("non-canonical-json", "Trust policy must already be canonical JSON.");
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "policyVersion", "minimumAcceptedPolicyVersion", "acceptedHistoricalPolicyVersions", "storageAuthorities", "keys");
        var schemaId = Cp6ReleaseJsonRules.RequireString(root, "$schemaId", "schema-id");
        if (!string.Equals(schemaId, Cp6ReleaseContractIds.PinnedTrustStore, StringComparison.Ordinal)) throw Error("schema-id", "Pinned trust schema ID is invalid.");
        var policyVersion = RequirePositiveInt(root, "policyVersion");
        var minimum = RequirePositiveInt(root, "minimumAcceptedPolicyVersion");
        if (minimum > policyVersion) throw Error("trust-policy-downgrade", "Minimum accepted policy exceeds current policy.");

        var historicalElement = Cp6ReleaseJsonRules.RequireProperty(root, "acceptedHistoricalPolicyVersions", JsonValueKind.Array);
        var historical = historicalElement.EnumerateArray().Select(item =>
        {
            if (!item.TryGetInt32(out var value) || value < 1) throw Error("policy-version", "Historical policy versions must be positive integers.");
            return value;
        }).ToArray();
        if (historical.Length != historical.Distinct().Count() || !historical.SequenceEqual(historical.Order()))
            throw Error("policy-version", "Historical policy versions must be unique and sorted.");

        ValidateStorageAuthorities(Cp6ReleaseJsonRules.RequireProperty(root, "storageAuthorities", JsonValueKind.Array));
        var keys = ParseKeys(Cp6ReleaseJsonRules.RequireProperty(root, "keys", JsonValueKind.Array));
        var validated = new Cp6ValidatedReleaseDocument(
            schemaId,
            null,
            null,
            null,
            Cp6DeterministicJson.Sha256Hex(canonical),
            [],
            [],
            keys.Keys.Order(StringComparer.Ordinal).ToArray(),
            canonical.ToArray());
        return new(policyVersion, minimum, historical.ToHashSet(), keys, validated);
    }

    public Cp6PinnedTrustKey RequireKey(
        string keyId,
        string purpose,
        int policyVersion,
        DateTimeOffset signedAtUtc,
        DateTimeOffset evaluationUtc,
        Cp6ReleaseValidationMode mode)
    {
        if (mode == Cp6ReleaseValidationMode.Current && policyVersion < MinimumAcceptedPolicyVersion)
            throw Error("trust-policy-downgrade", "Policy version is below the pinned minimum.");
        if (policyVersion > PolicyVersion ||
            (mode == Cp6ReleaseValidationMode.HistoricalAudit && policyVersion != PolicyVersion && !AcceptedHistoricalPolicyVersions.Contains(policyVersion)))
            throw Error("trust-policy-version", "Policy version is not pinned.");
        if (!_keys.TryGetValue(keyId, out var key)) throw Error("trust-key", "Key is not pinned.");
        if (!string.Equals(key.Purpose, purpose, StringComparison.Ordinal)) throw Error("trust-purpose", "Key purpose does not match.");
        if (signedAtUtc < key.ValidFromUtc || signedAtUtc > key.ValidUntilUtc ||
            (key.RevokedAtUtc is not null && signedAtUtc >= key.RevokedAtUtc.Value))
            throw Error("trust-validity", "Key was not valid at signing time.");
        if (mode == Cp6ReleaseValidationMode.Current && key.RevokedAtUtc is not null && evaluationUtc >= key.RevokedAtUtc.Value)
            throw Error("trust-revoked", "Key is currently revoked.");
        return key;
    }

    public Cp6HistoricalKeyEvaluation EvaluateHistoricalKey(
        string keyId,
        string purpose,
        int policyVersion,
        DateTimeOffset signedAtUtc,
        DateTimeOffset evaluationUtc)
    {
        if (!_keys.TryGetValue(keyId, out var key) || !string.Equals(key.Purpose, purpose, StringComparison.Ordinal))
            return new(false, false, policyVersion <= PolicyVersion);
        var validAtSigning = signedAtUtc >= key.ValidFromUtc && signedAtUtc <= key.ValidUntilUtc &&
                             (key.RevokedAtUtc is null || signedAtUtc < key.RevokedAtUtc.Value);
        var currentlyRevoked = key.RevokedAtUtc is not null && evaluationUtc >= key.RevokedAtUtc.Value;
        return new(validAtSigning, currentlyRevoked, policyVersion == PolicyVersion || AcceptedHistoricalPolicyVersions.Contains(policyVersion));
    }

    private static void ValidateStorageAuthorities(JsonElement authorities)
    {
        var ids = new List<string>();
        foreach (var authority in authorities.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(authority, "id", "endpoint", "bucket");
            var id = Cp6ReleaseJsonRules.RequireString(authority, "id", "storage-authority");
            if (!string.Equals(id, "cp6-release-r2-v1", StringComparison.Ordinal)) throw Error("storage-authority", "Storage authority is not pinned.");
            var endpoint = Cp6ReleaseJsonRules.RequireString(authority, "endpoint", "storage-endpoint");
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw Error("storage-endpoint", "Storage endpoint must be HTTPS.");
            _ = Cp6ReleaseJsonRules.RequireString(authority, "bucket", "storage-bucket");
            ids.Add(id);
        }
        Cp6ReleaseJsonRules.RequireOrdinalSet(ids, "storage-authority");
        if (ids.Count != 1) throw Error("storage-authority", "Exactly one fixed storage authority is required.");
    }

    private static IReadOnlyDictionary<string, Cp6PinnedTrustKey> ParseKeys(JsonElement keysElement)
    {
        var keys = new Dictionary<string, Cp6PinnedTrustKey>(StringComparer.Ordinal);
        foreach (var element in keysElement.EnumerateArray())
        {
            var hasRevocation = element.TryGetProperty("revokedAtUtc", out _);
            Cp6ReleaseJsonRules.RequireExactObject(
                element,
                hasRevocation
                    ? ["keyId", "purpose", "validFromUtc", "validUntilUtc", "publicKey", "revokedAtUtc", "revocationReason"]
                    : ["keyId", "purpose", "validFromUtc", "validUntilUtc", "publicKey"]);
            var keyId = Cp6ReleaseJsonRules.RequireString(element, "keyId", "trust-key");
            if (!keyId.StartsWith("sha256:", StringComparison.Ordinal)) throw Error("trust-key", "Key ID must be SHA-256 based.");
            Cp6ReleaseJsonRules.RequireSha256(keyId[7..], "trust-key");
            var purpose = Cp6ReleaseJsonRules.RequireString(element, "purpose", "trust-purpose");
            if (purpose is not ("oci" or "candidate-locator")) throw Error("trust-purpose", "Key purpose is not approved.");
            var validFrom = ParseUtc(element, "validFromUtc");
            var validUntil = ParseUtc(element, "validUntilUtc");
            if (validUntil <= validFrom) throw Error("trust-validity", "Key validity interval is empty.");
            var publicKey = Cp6ReleaseJsonRules.RequireString(element, "publicKey", "trust-public-key");
            DateTimeOffset? revokedAt = null;
            string? reason = null;
            if (hasRevocation)
            {
                revokedAt = ParseUtc(element, "revokedAtUtc");
                reason = Cp6ReleaseJsonRules.RequireString(element, "revocationReason", "trust-revocation");
            }
            if (!keys.TryAdd(keyId, new(keyId, purpose, validFrom, validUntil, publicKey, revokedAt, reason)))
                throw Error("trust-key", "Duplicate pinned key ID.");
        }
        var ordered = keys.Keys.Order(StringComparer.Ordinal).ToArray();
        if (!keys.Keys.SequenceEqual(ordered, StringComparer.Ordinal)) throw Error("trust-key", "Pinned keys must be ordinal-sorted.");
        return keys;
    }

    private static int RequirePositiveInt(JsonElement value, string name)
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

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);
}

public sealed record Cp6PinnedTrustKey(
    string KeyId,
    string Purpose,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    string PublicKey,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason);

public sealed record Cp6HistoricalKeyEvaluation(bool WasValidAtSigning, bool CurrentlyRevoked, bool PolicyWasPinned);
