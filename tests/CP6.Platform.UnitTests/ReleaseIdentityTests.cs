using CP6.Platform.Contracts;

namespace CP6.Platform.UnitTests;

public sealed class ReleaseIdentityTests
{
    [Fact]
    public void Candidate_RequiresImmutableIdentity()
    {
        var identity = Candidate();

        identity.Validate();

        Assert.True(identity.Candidate);
        Assert.Equal(Cp6ReleaseMode.Candidate, identity.Mode);
    }

    [Theory]
    [InlineData("CRM-api", "0.8.0-alpha.1", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8.0+local", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8.0-alpha.1", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8.0-alpha.1", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8.0-alpha.1", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8.0-alpha.1", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha512:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
    [InlineData("crm-api", "0.8.0-alpha.1", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "sha256:cccc")]
    public void Candidate_RejectsMutableOrNonCanonicalIdentity(
        string service,
        string version,
        string gitSha,
        string artifactDigest,
        string contractBundleDigest)
    {
        var identity = new Cp6ReleaseIdentity(
            service,
            version,
            gitSha,
            artifactDigest,
            contractBundleDigest,
            Cp6ReleaseMode.Candidate);

        Assert.Throws<ArgumentException>(identity.Validate);
    }

    [Fact]
    public void NonCandidate_AllowsExplicitLocalIdentityWithoutDigests()
    {
        var identity = new Cp6ReleaseIdentity(
            "crm-api",
            "local",
            string.Empty,
            string.Empty,
            string.Empty,
            Cp6ReleaseMode.NonCandidate);

        identity.Validate();

        Assert.False(identity.Candidate);
    }

    [Fact]
    public void NonCandidate_ValidatesAnyDigestThatIsPresent()
    {
        var identity = new Cp6ReleaseIdentity(
            "crm-api",
            "local",
            string.Empty,
            "not-a-digest",
            string.Empty,
            Cp6ReleaseMode.NonCandidate);

        Assert.Throws<ArgumentException>(identity.Validate);
    }

    [Fact]
    public void ValidationFailure_DoesNotEchoRejectedIdentity()
    {
        var identity = Candidate() with { Service = "Secret-Service" };

        var exception = Assert.Throws<ArgumentException>(identity.Validate);

        Assert.DoesNotContain("Secret-Service", exception.Message, StringComparison.Ordinal);
    }

    private static Cp6ReleaseIdentity Candidate() =>
        new(
            "crm-api",
            "0.8.0-alpha.1",
            new string('a', 40),
            $"sha256:{new string('b', 64)}",
            $"sha256:{new string('c', 64)}",
            Cp6ReleaseMode.Candidate);
}
