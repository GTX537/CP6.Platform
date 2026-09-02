using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class FormalCertificateProfileTests
{
    [Fact]
    public void Exact_formal_certificate_profile_is_accepted_and_derived_from_der()
    {
        var certificate = FormalCertificateTestData.CreateCertificate();

        var identity = Cp6NuGetCertificateProfile.Validate(certificate.Der);

        Assert.Equal(certificate.Fingerprint, identity.CertificateSha256);
        Assert.Equal(certificate.SpkiKeyId, identity.SpkiKeyId);
        Assert.Equal("CN=CP6 Platform Release Signing", identity.Subject);
        Assert.Equal(identity.Subject, identity.Issuer);
    }

    [Fact]
    public void Test_subject_is_not_a_formal_signer()
    {
        var certificate = FormalCertificateTestData.CreateCertificate("CN=CP6 Platform Test Package Signing");

        Assert.Throws<Cp6ReleaseContractException>(() => Cp6NuGetCertificateProfile.Validate(certificate.Der));
    }

    [Fact]
    public void Weak_ca_missing_eku_and_missing_ski_profiles_are_rejected()
    {
        var invalid = new[]
        {
            FormalCertificateTestData.CreateCertificate(keySize: 2048),
            FormalCertificateTestData.CreateCertificate(certificateAuthority: true),
            FormalCertificateTestData.CreateCertificate(includeCodeSigningEku: false),
            FormalCertificateTestData.CreateCertificate(includeSubjectKeyIdentifier: false)
        };

        foreach (var certificate in invalid)
        {
            Assert.Equal("certificate-profile", Assert.Throws<Cp6ReleaseContractException>(() =>
                Cp6NuGetCertificateProfile.Validate(certificate.Der)).Code);
        }
    }

    [Fact]
    public void Current_use_requires_activation_and_certificate_validity_at_signing_and_evaluation()
    {
        var fixture = FormalCertificateTestData.CreatePolicy();
        var policy = Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates);
        var signer = policy.CurrentSigner;

        Assert.Throws<Cp6ReleaseContractException>(() => policy.RequireSigner(
            signer.CertificateSha256,
            signer.ValidFromUtc.AddMilliseconds(-1),
            signer.ActivatedAtUtc,
            Cp6ReleaseValidationMode.Current));
        Assert.Throws<Cp6ReleaseContractException>(() => policy.RequireSigner(
            signer.CertificateSha256,
            signer.ActivatedAtUtc,
            signer.ValidUntilUtc.AddMilliseconds(1),
            Cp6ReleaseValidationMode.Current));
    }

    [Fact]
    public void Revoked_signer_is_auditable_but_never_currently_consumable()
    {
        var current = FormalCertificateTestData.CreateCertificate();
        var revoked = FormalCertificateTestData.CreateCertificate();
        var revokedAt = revoked.NotBeforeUtc.AddDays(30);
        var fixture = FormalCertificateTestData.CreatePolicyWithSigners(
            new SignerSpec(current, "Current"),
            new SignerSpec(revoked, "Revoked", revokedAt));
        var policy = Cp6PinnedNuGetTrustPolicy.Parse(fixture.CanonicalBytes(), fixture.Certificates);
        var signedAt = revoked.NotBeforeUtc.AddDays(1);

        Assert.Equal("trust-revoked", Assert.Throws<Cp6ReleaseContractException>(() => policy.RequireSigner(
            revoked.Fingerprint,
            signedAt,
            revokedAt.AddDays(1),
            Cp6ReleaseValidationMode.Current)).Code);
        Assert.Equal("Revoked", policy.RequireSigner(
            revoked.Fingerprint,
            signedAt,
            revokedAt.AddDays(1),
            Cp6ReleaseValidationMode.HistoricalAudit).Status);
        var historical = policy.EvaluateHistoricalSigner(revoked.Fingerprint, signedAt, revokedAt.AddDays(1));
        Assert.True(historical.WasValidAtSigning);
        Assert.True(historical.CurrentlyRevoked);
    }
}
