using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class PinnedNuGetTrustPolicyTests
{
    [Fact]
    public void Canonical_policy_and_content_addressed_certificate_define_exact_trust_claims()
    {
        var fixture = FormalCertificateTestData.CreatePolicy();

        var policy = Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates);

        Assert.Equal("PinnedSelfSigned", policy.TrustModel);
        Assert.False(policy.PublicCaTrusted);
        Assert.True(policy.InternallyTrusted);
        Assert.Equal("Rfc3161Required", policy.TimestampPolicy);
        Assert.Equal(new Uri("http://timestamp.digicert.com"), policy.TimestampService);
        Assert.Equal(7, policy.AllowedPackageIds.Count);
        Assert.Equal("Current", policy.CurrentSigner.Status);
        Assert.Same(
            policy.CurrentSigner,
            policy.RequireSigner(
                policy.CurrentSigner.CertificateSha256,
                policy.CurrentSigner.ActivatedAtUtc,
                policy.CurrentSigner.ActivatedAtUtc,
                Cp6ReleaseValidationMode.Current));
    }

    [Theory]
    [InlineData("trustModel", "PublicCa")]
    [InlineData("publicCaTrusted", true)]
    [InlineData("internallyTrusted", false)]
    [InlineData("timestampPolicy", "TestOnlyNone")]
    [InlineData("timestampService", "https://example.invalid")]
    public void Fixed_trust_claim_mutations_are_rejected(string property, object value)
    {
        var fixture = FormalCertificateTestData.CreatePolicy();
        fixture.Root[property] = JsonValue.Create(value);

        Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates));
    }

    [Fact]
    public void Package_set_must_be_the_exact_ordinal_seven()
    {
        var fixture = FormalCertificateTestData.CreatePolicy();
        var packages = fixture.Root["allowedPackageIds"]!.AsArray();
        var first = packages[0]!.GetValue<string>();
        var second = packages[1]!.GetValue<string>();
        packages[0] = second;
        packages[1] = first;

        Assert.Equal("package-set", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates)).Code);
    }

    [Fact]
    public void Exactly_one_current_signer_is_required()
    {
        var fixture = FormalCertificateTestData.CreatePolicy(
            FormalCertificateTestData.CreateCertificate(),
            FormalCertificateTestData.CreateCertificate());

        Assert.Equal("signer-set", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates)).Code);
    }

    [Fact]
    public void Certificate_filename_hash_and_der_must_be_one_identity()
    {
        var fixture = FormalCertificateTestData.CreatePolicy();
        var signer = fixture.Root["signers"]!.AsArray()[0]!.AsObject();
        signer["certificateSha256"] = new string('f', 64);

        Assert.Equal("signer-identity", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates)).Code);
    }

    [Fact]
    public void Certificate_resolver_bytes_cannot_substitute_another_key()
    {
        var fixture = FormalCertificateTestData.CreatePolicy();
        var path = fixture.Root["signers"]!.AsArray()[0]!["certificatePath"]!.GetValue<string>();
        fixture.Certificates[path] = FormalCertificateTestData.CreateCertificate().Der;

        Assert.Equal("signer-identity", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates)).Code);
    }

    [Fact]
    public void Noncanonical_policy_bytes_fail_before_certificate_resolution()
    {
        var bytes = ReleaseTestData.Fixture("supporting", "pinned-nuget-trust-noncanonical.invalid.json");

        Assert.Equal("non-canonical-json", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedNuGetTrustPolicy.Parse(bytes, new Dictionary<string, ReadOnlyMemory<byte>>())).Code);
    }
}

internal static class FormalCertificateTestData
{
    private static readonly DateTimeOffset NotBefore = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotAfter = new(2028, 9, 1, 0, 5, 0, TimeSpan.Zero);

    public static PolicyFixture CreatePolicy(params TestCertificate[] currentCertificates)
    {
        if (currentCertificates.Length == 0)
        {
            currentCertificates = [CreateCertificate()];
        }

        return CreatePolicyWithSigners(currentCertificates.Select(certificate => new SignerSpec(certificate, "Current")).ToArray());
    }

    public static PolicyFixture CreatePolicyWithSigners(params SignerSpec[] signers)
    {
        var root = JsonNode.Parse(ReleaseTestData.Fixture("supporting", "pinned-nuget-trust.valid.json"))!.AsObject();
        var signerArray = new JsonArray();
        var certificates = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var specification in signers)
        {
            var certificate = specification.Certificate;
            var path = $"certificates/{certificate.Fingerprint}.cer";
            var revokedAt = specification.RevokedAtUtc ?? certificate.NotBeforeUtc.AddDays(30);
            signerArray.Add(new JsonObject
            {
                ["certificatePath"] = path,
                ["certificateSha256"] = certificate.Fingerprint,
                ["spkiKeyId"] = certificate.SpkiKeyId,
                ["subject"] = certificate.Subject,
                ["issuer"] = certificate.Issuer,
                ["validFromUtc"] = Utc(certificate.NotBeforeUtc),
                ["validUntilUtc"] = Utc(certificate.NotAfterUtc),
                ["status"] = specification.Status,
                ["activatedAtUtc"] = Utc(certificate.NotBeforeUtc.AddMinutes(5)),
                ["revokedAtUtc"] = specification.Status == "Revoked" ? Utc(revokedAt) : null,
                ["revocationReason"] = specification.Status == "Revoked" ? "Test revocation" : null
            });
            certificates.Add(path, certificate.Der);
        }

        root["signers"] = signerArray;
        return new PolicyFixture(root, certificates);
    }

    public static TestCertificate CreateCertificate(
        string subject = "CN=CP6 Platform Release Signing",
        int keySize = 3072,
        bool certificateAuthority = false,
        bool includeCodeSigningEku = true,
        bool includeSubjectKeyIdentifier = true)
    {
        using var rsa = RSA.Create(keySize);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        if (includeCodeSigningEku)
        {
            var usages = new OidCollection { new("1.3.6.1.5.5.7.3.3") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
        }

        if (includeSubjectKeyIdentifier)
        {
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        }

        using var generated = request.CreateSelfSigned(NotBefore, NotAfter);
        var der = generated.Export(X509ContentType.Cert);
        using var certificate = new X509Certificate2(der);
        using var publicKey = certificate.GetRSAPublicKey()!;
        var fingerprint = Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();
        var spkiKeyId = "sha256:" + Convert.ToHexString(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        return new(
            der,
            fingerprint,
            spkiKeyId,
            certificate.SubjectName.Name!,
            certificate.IssuerName.Name!,
            new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
    }

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

internal sealed record TestCertificate(
    byte[] Der,
    string Fingerprint,
    string SpkiKeyId,
    string Subject,
    string Issuer,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc);

internal sealed record SignerSpec(TestCertificate Certificate, string Status, DateTimeOffset? RevokedAtUtc = null);

internal sealed record PolicyFixture(
    JsonObject Root,
    Dictionary<string, ReadOnlyMemory<byte>> Certificates)
{
    public byte[] CanonicalBytes() =>
        Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(Root.ToJsonString()));
}
