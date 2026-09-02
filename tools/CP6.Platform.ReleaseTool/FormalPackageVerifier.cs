using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.Release;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Versioning;
using NuGetHashAlgorithmName = NuGet.Common.HashAlgorithmName;

internal static class FormalPackageVerifier
{
    private const string TimestampAttributeOid = "1.2.840.113549.1.9.16.2.14";
    private const string TimestampEkuOid = "1.3.6.1.5.5.7.3.8";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    public static Cp6PinnedNuGetTrustPolicy LoadTrustPolicy(string policyPath, string certificateDirectory)
    {
        var files = Directory.GetFiles(certificateDirectory, "*.cer", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var certificates = files.ToDictionary(
            path => $"certificates/{Path.GetFileName(path)}",
            path => (ReadOnlyMemory<byte>)File.ReadAllBytes(path),
            StringComparer.Ordinal);
        var policy = Cp6PinnedNuGetTrustPolicy.Parse(File.ReadAllBytes(policyPath), certificates);
        if (files.Length != policy.Signers.Count || policy.Signers.Any(signer => !certificates.ContainsKey(signer.CertificatePath)))
        {
            throw Error("trust-certificate-set", "Certificate directory must exactly match the pinned signer set.");
        }

        return policy;
    }

    public static async Task<FormalPackageVerificationResult> VerifyAsync(
        string packagePath,
        string policyPath,
        string certificateDirectory,
        string expectedPackageId,
        string expectedVersion,
        string expectedSourceGitSha,
        DateTimeOffset evaluationUtc,
        Cp6ReleaseValidationMode mode,
        CancellationToken cancellationToken)
    {
        ValidateExpectedVersion(expectedVersion);
        if (expectedSourceGitSha.Length != 40 || expectedSourceGitSha.Any(character => character is < '0' or > '9' and < 'a' or > 'f'))
        {
            throw Error("package-source", "Expected source SHA must be lowercase 40-character hexadecimal.");
        }
        var policy = LoadTrustPolicy(policyPath, certificateDirectory);
        if (!policy.AllowedPackageIds.Contains(expectedPackageId, StringComparer.Ordinal))
        {
            throw Error("package-id", "Requested package ID is outside the pinned formal package set.");
        }

        using var package = new PackageArchiveReader(packagePath);
        var signature = await package.GetPrimarySignatureAsync(cancellationToken);
        if (signature is not AuthorPrimarySignature authorSignature)
        {
            throw Error("package-signature", "Formal package requires an author primary signature.");
        }

        if (authorSignature.Timestamps.Count != 1)
        {
            throw Error("timestamp-count", "Formal package requires exactly one RFC3161 timestamp.");
        }

        var fingerprint = authorSignature
            .GetSigningCertificateFingerprint(NuGetHashAlgorithmName.SHA256)
            .ToLowerInvariant();
        var timestamp = authorSignature.Timestamps[0];
        var signer = policy.RequireSigner(fingerprint, timestamp.GeneralizedTime, evaluationUtc, mode);
        await VerifyNuGetSignatureAsync(package, signer.CertificateSha256, cancellationToken);
        var timestampIdentity = VerifyTimestamp(authorSignature, timestamp);
        await VerifyPackageIdentityAsync(
            package,
            expectedPackageId,
            expectedVersion,
            expectedSourceGitSha,
            cancellationToken);

        return new(
            expectedPackageId,
            expectedVersion,
            expectedSourceGitSha,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant(),
            signer.CertificateSha256,
            signer.SpkiKeyId,
            timestampIdentity.PolicyOid,
            timestampIdentity.GeneralizedTimeUtc,
            timestampIdentity.CertificateChainSha256);
    }

    private static async Task VerifyNuGetSignatureAsync(
        PackageArchiveReader package,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var normalizedFingerprint = fingerprint.ToUpperInvariant();
        var allowList = new VerificationAllowListEntry[]
        {
            new CertificateHashAllowListEntry(
                VerificationTarget.Author,
                SignaturePlacement.PrimarySignature,
                normalizedFingerprint,
                NuGetHashAlgorithmName.SHA256)
        };
        var allowUntrustedRootList = new[]
        {
            new KeyValuePair<string, NuGetHashAlgorithmName>(normalizedFingerprint, NuGetHashAlgorithmName.SHA256)
        };
        var providers = new ISignatureVerificationProvider[]
        {
            new IntegrityVerificationProvider(),
            new SignatureTrustAndValidityVerificationProvider(allowUntrustedRootList),
            new AllowListVerificationProvider(allowList, requireNonEmptyAllowList: true)
        };
        var settings = new SignedPackageVerifierSettings(
            allowUnsigned: false,
            allowIllegal: false,
            allowUntrusted: false,
            allowIgnoreTimestamp: false,
            allowMultipleTimestamps: false,
            allowNoTimestamp: false,
            allowUnknownRevocation: false,
            reportUnknownRevocation: true,
            verificationTarget: VerificationTarget.Author,
            signaturePlacement: SignaturePlacement.PrimarySignature,
            repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.Never,
            revocationMode: RevocationMode.Online);
        var verifier = new PackageSignatureVerifier(providers);
        var result = await verifier.VerifySignaturesAsync(package, settings, cancellationToken);
        if (!result.IsSigned || !result.IsValid)
        {
            throw Error("package-signature", "NuGet signature, timestamp, integrity, or pinned-author verification failed.");
        }
    }

    private static TimestampIdentity VerifyTimestamp(AuthorPrimarySignature signature, Timestamp timestamp)
    {
        var attributes = signature.SignerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>().ToArray();
        if (attributes.Length != 1 ||
            !string.Equals(attributes[0].Oid.Value, TimestampAttributeOid, StringComparison.Ordinal) ||
            attributes[0].Values.Count != 1 ||
            timestamp.SignedCms is null)
        {
            throw Error("timestamp-attribute", "Formal signature must contain exactly one RFC3161 unsigned attribute value.");
        }

        timestamp.SignedCms.CheckSignature(verifySignatureOnly: true);
        var tstInfo = ReadTimestampInfo(timestamp.SignedCms.ContentInfo.Content);
        if (!string.Equals(tstInfo.HashAlgorithmOid, Sha256Oid, StringComparison.Ordinal))
        {
            throw Error("timestamp-imprint", "RFC3161 message imprint must use SHA-256.");
        }

        var signatureValue = signature.GetSignatureValue();
        if (signatureValue is null || !SHA256.HashData(signatureValue).AsSpan().SequenceEqual(tstInfo.MessageImprint))
        {
            throw Error("timestamp-imprint", "RFC3161 message imprint does not bind the author signature.");
        }

        if (tstInfo.GeneralizedTimeUtc != timestamp.GeneralizedTime)
        {
            throw Error("timestamp-time", "RFC3161 generalized time does not match the verified NuGet timestamp.");
        }

        var leaf = timestamp.SignerInfo.Certificate ??
            throw Error("timestamp-certificate", "RFC3161 token does not contain a timestamp signer certificate.");
        var ekuExtensions = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
        if (ekuExtensions.Length != 1 ||
            !ekuExtensions[0].EnhancedKeyUsages.Cast<Oid>().Any(usage =>
                string.Equals(usage.Value, TimestampEkuOid, StringComparison.Ordinal)))
        {
            throw Error("timestamp-certificate", "Timestamp signer certificate lacks the time-stamping EKU.");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = tstInfo.GeneralizedTimeUtc.UtcDateTime;
        chain.ChainPolicy.DisableCertificateDownloads = false;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(TimestampEkuOid));
        foreach (var certificate in timestamp.SignedCms.Certificates)
        {
            if (!certificate.RawData.AsSpan().SequenceEqual(leaf.RawData))
            {
                chain.ChainPolicy.ExtraStore.Add(certificate);
            }
        }

        if (!chain.Build(leaf))
        {
            throw Error("timestamp-chain", "RFC3161 timestamp certificate does not build to a system root with online revocation.");
        }

        var chainHashes = chain.ChainElements.Cast<X509ChainElement>()
            .Select(element => Convert.ToHexString(SHA256.HashData(element.Certificate.RawData)).ToLowerInvariant())
            .ToArray();
        return new(tstInfo.PolicyOid, tstInfo.GeneralizedTimeUtc, chainHashes);
    }

    private static ParsedTimestampInfo ReadTimestampInfo(ReadOnlyMemory<byte> content)
    {
        try
        {
            var reader = new AsnReader(content, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            _ = sequence.ReadInteger();
            var policyOid = sequence.ReadObjectIdentifier();
            var imprint = sequence.ReadSequence();
            var algorithm = imprint.ReadSequence();
            var algorithmOid = algorithm.ReadObjectIdentifier();
            while (algorithm.HasData)
            {
                _ = algorithm.ReadEncodedValue();
            }

            var messageImprint = imprint.ReadOctetString();
            imprint.ThrowIfNotEmpty();
            _ = sequence.ReadIntegerBytes();
            var generalizedTime = sequence.ReadGeneralizedTime();
            reader.ThrowIfNotEmpty();
            if (string.IsNullOrWhiteSpace(policyOid))
            {
                throw Error("timestamp-policy", "RFC3161 timestamp policy OID is empty.");
            }

            return new(policyOid, algorithmOid, messageImprint, generalizedTime.ToUniversalTime());
        }
        catch (Cp6ReleaseContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is AsnContentException or InvalidOperationException)
        {
            throw Error("timestamp-token", "RFC3161 TSTInfo is malformed.");
        }
    }

    private static async Task VerifyPackageIdentityAsync(
        PackageArchiveReader package,
        string expectedPackageId,
        string expectedVersion,
        string expectedSourceGitSha,
        CancellationToken cancellationToken)
    {
        var nuspec = await package.GetNuspecReaderAsync(cancellationToken);
        var identity = nuspec.GetIdentity();
        if (!string.Equals(identity.Id, expectedPackageId, StringComparison.Ordinal) ||
            !string.Equals(identity.Version.ToNormalizedString(), expectedVersion, StringComparison.Ordinal))
        {
            throw Error("package-identity", "NuSpec package ID or version does not match the requested formal identity.");
        }

        var repository = nuspec.GetRepositoryMetadata();
        if (repository is null || !string.Equals(repository.Commit, expectedSourceGitSha, StringComparison.Ordinal))
        {
            throw Error("package-source", "NuSpec repository commit does not match the requested source SHA.");
        }
    }

    private static void ValidateExpectedVersion(string expectedVersion)
    {
        if (!NuGetVersion.TryParse(expectedVersion, out var parsed) ||
            parsed.IsPrerelease ||
            !string.IsNullOrEmpty(parsed.Metadata) ||
            !string.Equals(expectedVersion, parsed.ToNormalizedString(), StringComparison.Ordinal))
        {
            throw Error("package-version", "Formal package version must be canonical stable SemVer.");
        }
    }

    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);

    private sealed record ParsedTimestampInfo(
        string PolicyOid,
        string HashAlgorithmOid,
        byte[] MessageImprint,
        DateTimeOffset GeneralizedTimeUtc);

    private sealed record TimestampIdentity(
        string PolicyOid,
        DateTimeOffset GeneralizedTimeUtc,
        IReadOnlyList<string> CertificateChainSha256);
}

internal sealed record FormalPackageVerificationResult(
    string PackageId,
    string Version,
    string SourceGitSha,
    string PackageSha256,
    string SignerFingerprint,
    string SpkiKeyId,
    string TimestampPolicyOid,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<string> TimestampCertificateChainSha256);
