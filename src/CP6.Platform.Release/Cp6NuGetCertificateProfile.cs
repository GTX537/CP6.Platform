using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CP6.Platform.Release;

public static class Cp6NuGetCertificateProfile
{
    private const string FormalSubject = "CN=CP6 Platform Release Signing";
    private const string Sha256WithRsaOid = "1.2.840.113549.1.1.11";
    private const string CodeSigningEkuOid = "1.3.6.1.5.5.7.3.3";

    public static Cp6NuGetCertificateIdentity Validate(ReadOnlySpan<byte> der)
    {
        try
        {
            using var certificate = new X509Certificate2(der);
            if (!string.Equals(certificate.SubjectName.Name, FormalSubject, StringComparison.Ordinal) ||
                !string.Equals(certificate.IssuerName.Name, certificate.SubjectName.Name, StringComparison.Ordinal))
            {
                throw Error("Formal certificate subject and issuer must be the exact self-signed identity.");
            }

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null || rsa.KeySize != 3072)
            {
                throw Error("Formal certificate must use RSA-3072.");
            }

            if (!string.Equals(certificate.SignatureAlgorithm.Value, Sha256WithRsaOid, StringComparison.Ordinal))
            {
                throw Error("Formal certificate must use SHA256withRSA.");
            }

            var basicConstraints = RequireSingle<X509BasicConstraintsExtension>(certificate);
            if (!basicConstraints.Critical || basicConstraints.CertificateAuthority)
            {
                throw Error("Formal certificate must contain a critical CA=false basic constraint.");
            }

            var keyUsage = RequireSingle<X509KeyUsageExtension>(certificate);
            if (!keyUsage.Critical || keyUsage.KeyUsages != X509KeyUsageFlags.DigitalSignature)
            {
                throw Error("Formal certificate must contain only critical DigitalSignature key usage.");
            }

            var enhancedKeyUsage = RequireSingle<X509EnhancedKeyUsageExtension>(certificate);
            var usages = enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>().Select(usage => usage.Value).ToArray();
            if (usages.Length != 1 || !string.Equals(usages[0], CodeSigningEkuOid, StringComparison.Ordinal))
            {
                throw Error("Formal certificate must contain only the code-signing EKU.");
            }

            var subjectKeyIdentifier = RequireSingle<X509SubjectKeyIdentifierExtension>(certificate);
            if (string.IsNullOrWhiteSpace(subjectKeyIdentifier.SubjectKeyIdentifier))
            {
                throw Error("Formal certificate must contain a subject key identifier.");
            }

            var rawData = certificate.RawData;
            var certificateSha256 = Convert.ToHexString(SHA256.HashData(rawData)).ToLowerInvariant();
            var spki = rsa.ExportSubjectPublicKeyInfo();
            var spkiKeyId = "sha256:" + Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
            return new(
                certificateSha256,
                spkiKeyId,
                certificate.SubjectName.Name!,
                certificate.IssuerName.Name!,
                new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
        }
        catch (Cp6ReleaseContractException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            throw Error("Formal certificate DER or extension profile is invalid.");
        }
    }

    private static T RequireSingle<T>(X509Certificate2 certificate)
        where T : X509Extension
    {
        var extensions = certificate.Extensions.OfType<T>().ToArray();
        if (extensions.Length != 1)
        {
            throw Error($"Formal certificate must contain exactly one {typeof(T).Name}.");
        }

        return extensions[0];
    }

    private static Cp6ReleaseContractException Error(string message) => new("certificate-profile", message);
}

public sealed record Cp6NuGetCertificateIdentity(
    string CertificateSha256,
    string SpkiKeyId,
    string Subject,
    string Issuer,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc);
