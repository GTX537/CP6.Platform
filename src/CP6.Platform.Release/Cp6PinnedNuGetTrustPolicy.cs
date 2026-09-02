using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.Release;

public sealed class Cp6PinnedNuGetTrustPolicy
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
    private static readonly Regex CertificatePathPattern = new(
        "^certificates/(?<fingerprint>[0-9a-f]{64})\\.cer$",
        RegexOptions.CultureInvariant);
    private readonly IReadOnlyDictionary<string, Cp6PinnedNuGetSigner> _signersByFingerprint;

    private Cp6PinnedNuGetTrustPolicy(
        int policyVersion,
        IReadOnlyList<Cp6PinnedNuGetSigner> signers,
        Cp6ValidatedReleaseDocument validatedDocument)
    {
        PolicyVersion = policyVersion;
        TrustModel = "PinnedSelfSigned";
        PublicCaTrusted = false;
        InternallyTrusted = true;
        TimestampPolicy = "Rfc3161Required";
        TimestampService = new Uri("http://timestamp.digicert.com");
        AllowedPackageIds = PlatformPackageIds;
        Signers = signers;
        CurrentSigner = signers.Single(signer => signer.Status == "Current");
        _signersByFingerprint = signers.ToDictionary(signer => signer.CertificateSha256, StringComparer.Ordinal);
        ValidatedDocument = validatedDocument;
    }

    public int PolicyVersion { get; }
    public string TrustModel { get; }
    public bool PublicCaTrusted { get; }
    public bool InternallyTrusted { get; }
    public string TimestampPolicy { get; }
    public Uri TimestampService { get; }
    public IReadOnlyList<string> AllowedPackageIds { get; }
    public IReadOnlyList<Cp6PinnedNuGetSigner> Signers { get; }
    public Cp6PinnedNuGetSigner CurrentSigner { get; }
    public Cp6ValidatedReleaseDocument ValidatedDocument { get; }

    public static Cp6PinnedNuGetTrustPolicy Parse(
        ReadOnlySpan<byte> utf8Json,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> certificatesByPath)
    {
        var canonical = Cp6DeterministicJson.Canonicalize(utf8Json);
        if (!utf8Json.SequenceEqual(canonical))
        {
            throw Error("non-canonical-json", "Pinned NuGet trust policy must already be canonical JSON.");
        }

        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(
            root,
            "$schemaId", "policyVersion", "trustModel", "publicCaTrusted", "internallyTrusted",
            "timestampPolicy", "timestampService", "allowedPackageIds", "signers");
        RequireExactString(root, "$schemaId", Cp6ReleaseContractIds.PinnedNuGetTrustStore, "schema-id");
        var policyVersion = RequirePositiveInt(root, "policyVersion");
        RequireExactString(root, "trustModel", "PinnedSelfSigned", "trust-claim");
        if (Cp6ReleaseJsonRules.RequireBoolean(root, "publicCaTrusted", "trust-claim"))
        {
            throw Error("trust-claim", "Pinned self-signed trust cannot claim public CA trust.");
        }

        if (!Cp6ReleaseJsonRules.RequireBoolean(root, "internallyTrusted", "trust-claim"))
        {
            throw Error("trust-claim", "Pinned self-signed trust must be explicitly internal.");
        }

        RequireExactString(root, "timestampPolicy", "Rfc3161Required", "trust-claim");
        RequireExactString(root, "timestampService", "http://timestamp.digicert.com", "trust-claim");
        ValidatePackageSet(Cp6ReleaseJsonRules.RequireProperty(root, "allowedPackageIds", JsonValueKind.Array));
        var signers = ParseSigners(
            Cp6ReleaseJsonRules.RequireProperty(root, "signers", JsonValueKind.Array),
            certificatesByPath);
        if (signers.Count(signer => signer.Status == "Current") != 1)
        {
            throw Error("signer-set", "Exactly one Current NuGet signer is required.");
        }

        var validated = new Cp6ValidatedReleaseDocument(
            Cp6ReleaseContractIds.PinnedNuGetTrustStore,
            null,
            null,
            null,
            Cp6DeterministicJson.Sha256Hex(canonical),
            [],
            PlatformPackageIds,
            signers.Select(signer => signer.CertificateSha256).Order(StringComparer.Ordinal).ToArray(),
            canonical.ToArray());
        return new(policyVersion, signers, validated);
    }

    public Cp6PinnedNuGetSigner RequireSigner(
        string fingerprint,
        DateTimeOffset signedAtUtc,
        DateTimeOffset evaluationUtc,
        Cp6ReleaseValidationMode mode)
    {
        Cp6ReleaseJsonRules.RequireSha256(fingerprint, "trust-signer");
        if (!_signersByFingerprint.TryGetValue(fingerprint, out var signer))
        {
            throw Error("trust-signer", "NuGet signer fingerprint is not pinned.");
        }

        if (signedAtUtc < signer.ActivatedAtUtc || signedAtUtc < signer.ValidFromUtc || signedAtUtc > signer.ValidUntilUtc ||
            (signer.RevokedAtUtc is not null && signedAtUtc >= signer.RevokedAtUtc.Value))
        {
            throw Error("signer-validity", "NuGet signer was not valid and active at signing time.");
        }

        if (mode == Cp6ReleaseValidationMode.Current)
        {
            if (signer.Status == "Revoked")
            {
                throw Error("trust-revoked", "A revoked NuGet signer is never currently consumable.");
            }

            if (signer.Status != "Current")
            {
                throw Error("signer-state", "Current verification requires the Current NuGet signer.");
            }

            if (evaluationUtc < signer.ActivatedAtUtc || evaluationUtc < signer.ValidFromUtc || evaluationUtc > signer.ValidUntilUtc)
            {
                throw Error("signer-validity", "Current verification is outside the signer validity interval.");
            }
        }

        return signer;
    }

    public Cp6HistoricalNuGetSignerEvaluation EvaluateHistoricalSigner(
        string fingerprint,
        DateTimeOffset signedAtUtc,
        DateTimeOffset evaluationUtc)
    {
        if (!_signersByFingerprint.TryGetValue(fingerprint, out var signer))
        {
            return new(false, false, false);
        }

        var validAtSigning = signedAtUtc >= signer.ActivatedAtUtc &&
            signedAtUtc >= signer.ValidFromUtc &&
            signedAtUtc <= signer.ValidUntilUtc &&
            (signer.RevokedAtUtc is null || signedAtUtc < signer.RevokedAtUtc.Value);
        var currentlyRevoked = signer.Status == "Revoked" &&
            signer.RevokedAtUtc is not null &&
            evaluationUtc >= signer.RevokedAtUtc.Value;
        return new(validAtSigning, currentlyRevoked, true);
    }

    private static IReadOnlyList<Cp6PinnedNuGetSigner> ParseSigners(
        JsonElement value,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> certificatesByPath)
    {
        var signers = new List<Cp6PinnedNuGetSigner>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in value.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(
                element,
                "certificatePath", "certificateSha256", "spkiKeyId", "subject", "issuer",
                "validFromUtc", "validUntilUtc", "status", "activatedAtUtc", "revokedAtUtc", "revocationReason");
            var path = Cp6ReleaseJsonRules.RequireString(element, "certificatePath", "signer-path");
            var pathMatch = CertificatePathPattern.Match(path);
            if (!pathMatch.Success)
            {
                throw Error("signer-path", "Certificate path is not lowercase content-addressed DER.");
            }

            var certificateSha256 = Cp6ReleaseJsonRules.RequireString(element, "certificateSha256", "signer-identity");
            Cp6ReleaseJsonRules.RequireSha256(certificateSha256, "signer-identity");
            var spkiKeyId = Cp6ReleaseJsonRules.RequireString(element, "spkiKeyId", "signer-identity");
            if (!spkiKeyId.StartsWith("sha256:", StringComparison.Ordinal))
            {
                throw Error("signer-identity", "SPKI key ID must be SHA-256 based.");
            }

            Cp6ReleaseJsonRules.RequireSha256(spkiKeyId[7..], "signer-identity");
            if (!certificatesByPath.TryGetValue(path, out var der))
            {
                throw Error("signer-certificate", "Pinned certificate bytes were not resolved by their policy path.");
            }

            var identity = Cp6NuGetCertificateProfile.Validate(der.Span);
            var subject = Cp6ReleaseJsonRules.RequireString(element, "subject", "signer-identity");
            var issuer = Cp6ReleaseJsonRules.RequireString(element, "issuer", "signer-identity");
            var validFrom = ParseUtc(element, "validFromUtc");
            var validUntil = ParseUtc(element, "validUntilUtc");
            if (!string.Equals(pathMatch.Groups["fingerprint"].Value, certificateSha256, StringComparison.Ordinal) ||
                !string.Equals(identity.CertificateSha256, certificateSha256, StringComparison.Ordinal) ||
                !string.Equals(identity.SpkiKeyId, spkiKeyId, StringComparison.Ordinal) ||
                !string.Equals(identity.Subject, subject, StringComparison.Ordinal) ||
                !string.Equals(identity.Issuer, issuer, StringComparison.Ordinal) ||
                identity.ValidFromUtc != validFrom ||
                identity.ValidUntilUtc != validUntil)
            {
                throw Error("signer-identity", "Policy claims, content-addressed path, and DER certificate do not match.");
            }

            var status = Cp6ReleaseJsonRules.RequireString(element, "status", "signer-state");
            if (status is not ("Current" or "Historical" or "Revoked"))
            {
                throw Error("signer-state", "NuGet signer status is not approved.");
            }

            var activatedAt = ParseUtc(element, "activatedAtUtc");
            if (validUntil <= validFrom || activatedAt < validFrom || activatedAt > validUntil)
            {
                throw Error("signer-validity", "NuGet signer activation or validity interval is invalid.");
            }

            var revokedAt = RequireNullableUtc(element, "revokedAtUtc");
            var revocationReason = RequireNullableString(element, "revocationReason");
            if (status == "Revoked")
            {
                if (revokedAt is null || string.IsNullOrWhiteSpace(revocationReason) || revokedAt < activatedAt || revokedAt > validUntil)
                {
                    throw Error("signer-state", "Revoked signer requires an effective revocation time and reason.");
                }
            }
            else if (revokedAt is not null || revocationReason is not null)
            {
                throw Error("signer-state", "Current and Historical signers require null revocation fields.");
            }

            if (!fingerprints.Add(certificateSha256) || !paths.Add(path))
            {
                throw Error("signer-set", "NuGet signer identities must be unique.");
            }

            signers.Add(new(
                path,
                certificateSha256,
                spkiKeyId,
                subject,
                issuer,
                validFrom,
                validUntil,
                status,
                activatedAt,
                revokedAt,
                revocationReason));
        }

        if (signers.Count == 0)
        {
            throw Error("signer-set", "At least one NuGet signer is required.");
        }

        return signers;
    }

    private static void ValidatePackageSet(JsonElement value)
    {
        var actual = value.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Error("package-set", "Allowed package IDs must be strings.");
            }

            return item.GetString()!;
        }).ToArray();
        if (!actual.SequenceEqual(PlatformPackageIds, StringComparer.Ordinal))
        {
            throw Error("package-set", "Pinned NuGet trust requires the exact seven package IDs in ordinal order.");
        }
    }

    private static void RequireExactString(JsonElement root, string name, string expected, string code)
    {
        var actual = Cp6ReleaseJsonRules.RequireString(root, name, code);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Error(code, $"Property '{name}' is not the fixed P10 value.");
        }
    }

    private static int RequirePositiveInt(JsonElement root, string name)
    {
        var value = Cp6ReleaseJsonRules.RequireNonNegativeInteger(root, name, "policy-version");
        if (value is < 1 or > int.MaxValue)
        {
            throw Error("policy-version", "Policy version must be a positive Int32.");
        }

        return (int)value;
    }

    private static DateTimeOffset ParseUtc(JsonElement root, string name)
    {
        var text = Cp6ReleaseJsonRules.RequireString(root, name, "utc-timestamp");
        Cp6ReleaseJsonRules.RequireUtcMilliseconds(text, "utc-timestamp");
        return DateTimeOffset.ParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static DateTimeOffset? RequireNullableUtc(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => ParseUtc(root, name),
            _ => throw Error("property-kind", $"Property '{name}' must be a UTC timestamp or null.")
        };
    }

    private static string? RequireNullableString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Error("property-kind", $"Property '{name}' must be a non-empty string or null.");
        }

        return value.GetString();
    }

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);
}

public sealed record Cp6PinnedNuGetSigner(
    string CertificatePath,
    string CertificateSha256,
    string SpkiKeyId,
    string Subject,
    string Issuer,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    string Status,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason);

public sealed record Cp6HistoricalNuGetSignerEvaluation(
    bool WasValidAtSigning,
    bool CurrentlyRevoked,
    bool SignerWasPinned);
