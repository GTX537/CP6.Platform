using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        switch (arguments)
        {
            case ["canonicalize", var input, var output]:
                File.WriteAllBytes(output, Cp6DeterministicJson.Canonicalize(File.ReadAllBytes(input)));
                return 0;
            case ["validate-build-provenance", var input]:
                Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(File.ReadAllBytes(input));
                return 0;
            case ["validate-evidence", var input]:
                Cp6SupportingContractValidator.ValidateEvidenceRecord(File.ReadAllBytes(input));
                return 0;
            case ["validate-transport", var input, var evaluationText]
                when TryParseUtcRoundTrip(evaluationText, out var evaluationUtc):
                Cp6SupportingContractValidator.ValidateTestPackageTransport(File.ReadAllBytes(input), evaluationUtc);
                return 0;
            case ["validate-transport", _, _]:
                return 64;
            case ["validate-nuget-trust", var policyPath, var certificateDirectory]:
                var policy = FormalPackageVerifier.LoadTrustPolicy(policyPath, certificateDirectory);
                Console.WriteLine(policy.ValidatedDocument.Sha256);
                return 0;
            case ["validate-formal-publication", var publicationPath, var policyPath, var certificateDirectory, var evaluationText]
                when TryParseCanonicalUtc(evaluationText, out var publicationEvaluationUtc):
                var publicationPolicy = FormalPackageVerifier.LoadTrustPolicy(policyPath, certificateDirectory);
                var publication = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
                    File.ReadAllBytes(publicationPath),
                    publicationPolicy,
                    publicationEvaluationUtc);
                Console.WriteLine(publication.Sha256);
                return 0;
            case ["validate-formal-publication", ..]:
                return 64;
            case ["verify-formal-package", var packagePath, var policyPath, var certificateDirectory,
                var packageId, var version, var sourceGitSha, var evaluationText, var modeText]
                when TryParseCanonicalUtc(evaluationText, out var evaluationUtc) &&
                     Enum.TryParse<Cp6ReleaseValidationMode>(modeText, ignoreCase: false, out var mode):
                var result = await FormalPackageVerifier.VerifyAsync(
                    packagePath,
                    policyPath,
                    certificateDirectory,
                    packageId,
                    version,
                    sourceGitSha,
                    evaluationUtc,
                    mode,
                    CancellationToken.None);
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
                return 0;
            case ["verify-formal-package", ..]:
                return 64;
            case ["download-package", var sourceUrl, var packageId, var version, var destinationPath]:
                await NuGetPackageDownloader.DownloadAsync(
                    sourceUrl,
                    packageId,
                    version,
                    destinationPath,
                    CancellationToken.None);
                return 0;
            case ["verify-test-package", var packagePath, var certificateFingerprint]
                when IsCanonicalSha256(certificateFingerprint):
                return await VerifyTestPackageAsync(packagePath, certificateFingerprint) ? 0 : 2;
            case ["verify-test-package", _, _]:
                return 64;
            default:
                return 64;
        }
    }
    catch (Cp6ReleaseContractException exception)
    {
        Console.Error.WriteLine($"{exception.Code}: {exception.Message}");
        return 2;
    }
    catch
    {
        Console.Error.WriteLine("release-tool-internal-error");
        return 1;
    }
}

static async Task<bool> VerifyTestPackageAsync(string packagePath, string certificateFingerprint)
{
    var normalizedFingerprint = certificateFingerprint.ToUpperInvariant();
    var allowList = new VerificationAllowListEntry[]
    {
        new CertificateHashAllowListEntry(
            VerificationTarget.Author,
            SignaturePlacement.PrimarySignature,
            normalizedFingerprint,
            HashAlgorithmName.SHA256)
    };
    var allowUntrustedRootList = new[]
    {
        new KeyValuePair<string, HashAlgorithmName>(normalizedFingerprint, HashAlgorithmName.SHA256)
    };
    var providers = new ISignatureVerificationProvider[]
    {
        new IntegrityVerificationProvider(),
        new SignatureTrustAndValidityVerificationProvider(allowUntrustedRootList),
        new AllowListVerificationProvider(allowList, requireNonEmptyAllowList: true)
    };

    using var package = new PackageArchiveReader(packagePath);
    var signature = await package.GetPrimarySignatureAsync(CancellationToken.None);
    if (signature is not AuthorPrimarySignature)
    {
        return false;
    }

    var verifier = new PackageSignatureVerifier(providers);
    var result = await verifier.VerifySignaturesAsync(
        package,
        SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy(),
        CancellationToken.None);
    return result.IsSigned && result.IsValid;
}

static bool IsCanonicalSha256(string value) =>
    value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

static bool TryParseUtcRoundTrip(string value, out DateTimeOffset result)
{
    result = default;
    return value.EndsWith('Z') &&
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result) &&
        result.Offset == TimeSpan.Zero;
}

static bool TryParseCanonicalUtc(string value, out DateTimeOffset result)
{
    result = default;
    return DateTimeOffset.TryParseExact(
        value,
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out result) &&
        result.Offset == TimeSpan.Zero;
}
