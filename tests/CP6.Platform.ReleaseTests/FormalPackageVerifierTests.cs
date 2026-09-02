using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class FormalPackageVerifierTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void Cli_validates_pinned_cosign_and_storage_trust()
    {
        var valid = Path.Combine(
            ReleaseTestData.RepositoryRoot,
            "contracts", "release", "v1", "fixtures", "supporting", "trust.valid.json");
        var invalid = Path.Combine(
            ReleaseTestData.RepositoryRoot,
            "contracts", "release", "v1", "fixtures", "supporting", "trust-authority.invalid.json");

        Assert.Equal(0, RunTool("validate-trust", valid).ExitCode);
        Assert.Equal(2, RunTool("validate-trust", invalid).ExitCode);
    }

    [Fact]
    public void Cli_validates_pinned_trust_without_accepting_the_S02_identity()
    {
        using var directory = new UnitDirectory();
        var formal = CreateSigningCertificate("CN=CP6 Platform Release Signing");
        var formalPolicy = WritePolicy(directory.Path, formal.PublicCertificate);

        Assert.Equal(0, RunTool("validate-nuget-trust", formalPolicy.PolicyPath, formalPolicy.CertificateDirectory).ExitCode);

        var testOnly = CreateSigningCertificate("CN=CP6 Platform Test Package Signing");
        var testOnlyPolicy = WritePolicy(Path.Combine(directory.Path, "s02"), testOnly.PublicCertificate);
        Assert.Equal(2, RunTool("validate-nuget-trust", testOnlyPolicy.PolicyPath, testOnlyPolicy.CertificateDirectory).ExitCode);
    }

    [Fact]
    public void Formal_verifier_accepts_one_real_RFC3161_timestamp_and_rejects_identity_and_integrity_mutations()
    {
        using var directory = new UnitDirectory();
        var sourceGitSha = Run("git", "rev-parse", "HEAD").StandardOutput.Trim();
        var unsignedPackage = PackReleasePackage(directory.Path, sourceGitSha);
        var noTimestampPackage = Path.Combine(directory.Path, "no-timestamp.nupkg");
        File.Copy(unsignedPackage, noTimestampPackage);

        var signing = CreateSigningCertificate("CN=CP6 Platform Release Signing");
        var pfxPath = Path.Combine(directory.Path, "synthetic-formal-signing.pfx");
        File.WriteAllBytes(pfxPath, signing.Pfx);
        var trust = WritePolicy(Path.Combine(directory.Path, "formal-trust"), signing.PublicCertificate);

        Assert.Equal(0, Run(
            "dotnet", "nuget", "sign", unsignedPackage,
            "--certificate-path", pfxPath,
            "--certificate-password", signing.Password,
            "--hash-algorithm", "SHA256",
            "--timestamper", "http://timestamp.digicert.com",
            "--timestamp-hash-algorithm", "SHA256",
            "--overwrite").ExitCode);
        Assert.Equal(0, Run(
            "dotnet", "nuget", "sign", noTimestampPackage,
            "--certificate-path", pfxPath,
            "--certificate-password", signing.Password,
            "--hash-algorithm", "SHA256",
            "--overwrite").ExitCode);

        var evaluationUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var validArguments = new[]
        {
            "verify-formal-package", unsignedPackage, trust.PolicyPath, trust.CertificateDirectory,
            "CP6.Platform.Release", "0.10.0", sourceGitSha, evaluationUtc, "Current"
        };
        var valid = RunTool(validArguments);
        Assert.True(
            valid.ExitCode == 0,
            $"verify-formal-package exit={valid.ExitCode}; stderr={valid.StandardError}; stdout={valid.StandardOutput}");

        Assert.Equal(2, RunTool(validArguments.WithArgument(4, "CP6.Platform.Contracts")).ExitCode);
        Assert.Equal(2, RunTool(validArguments.WithArgument(5, "0.10.1")).ExitCode);
        Assert.Equal(2, RunTool(validArguments.WithArgument(6, new string('a', 40))).ExitCode);
        Assert.Equal(2, RunTool(validArguments.WithArgument(1, noTimestampPackage)).ExitCode);

        var wrongTrust = WritePolicy(Path.Combine(directory.Path, "wrong-trust"), CreateSigningCertificate("CN=CP6 Platform Release Signing").PublicCertificate);
        var wrongTrustArguments = validArguments.WithArgument(2, wrongTrust.PolicyPath).WithArgument(3, wrongTrust.CertificateDirectory);
        Assert.Equal(2, RunTool(wrongTrustArguments).ExitCode);

        var tamperedPackage = Path.Combine(directory.Path, "tampered.nupkg");
        File.Copy(unsignedPackage, tamperedPackage);
        using (var archive = ZipFile.Open(tamperedPackage, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("tampered.txt").Open());
            writer.Write("tampered");
        }
        Assert.Equal(2, RunTool(validArguments.WithArgument(1, tamperedPackage)).ExitCode);
    }

    [Fact]
    public void Formal_verifier_source_freezes_strict_timestamp_and_download_rules()
    {
        var verifier = File.ReadAllText(Path.Combine(
            ReleaseTestData.RepositoryRoot, "tools", "CP6.Platform.ReleaseTool", "FormalPackageVerifier.cs"));
        var downloader = File.ReadAllText(Path.Combine(
            ReleaseTestData.RepositoryRoot, "tools", "CP6.Platform.ReleaseTool", "NuGetPackageDownloader.cs"));

        Assert.Contains("allowUnsigned: false", verifier, StringComparison.Ordinal);
        Assert.Contains("allowMultipleTimestamps: false", verifier, StringComparison.Ordinal);
        Assert.Contains("allowNoTimestamp: false", verifier, StringComparison.Ordinal);
        Assert.Contains("RevocationMode.Online", verifier, StringComparison.Ordinal);
        Assert.Contains("1.2.840.113549.1.9.16.2.14", verifier, StringComparison.Ordinal);
        Assert.Contains("1.3.6.1.5.5.7.3.8", verifier, StringComparison.Ordinal);
        Assert.Contains("FileMode.CreateNew", downloader, StringComparison.Ordinal);
        Assert.DoesNotContain("GITHUB_TOKEN", downloader, StringComparison.Ordinal);
    }

    [Fact]
    public void Downloader_streams_to_a_new_file_and_never_overwrites_existing_bytes()
    {
        using var directory = new UnitDirectory();
        var destination = Path.Combine(directory.Path, "NuGet.Versioning.6.11.2.nupkg");

        var first = RunTool(
            "download-package",
            "https://api.nuget.org/v3/index.json",
            "NuGet.Versioning",
            "6.11.2",
            destination);
        Assert.True(first.ExitCode == 0, first.StandardError + first.StandardOutput);
        var originalHash = SHA256.HashData(File.ReadAllBytes(destination));

        Assert.NotEqual(0, RunTool(
            "download-package",
            "https://api.nuget.org/v3/index.json",
            "NuGet.Versioning",
            "6.11.2",
            destination).ExitCode);
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(destination)));
    }

    private static string PackReleasePackage(string output, string sourceGitSha)
    {
        var result = Run(
            "dotnet", "pack", "src/CP6.Platform.Release/CP6.Platform.Release.csproj",
            "--configuration", "Release", "--no-restore",
            "-p:PackageVersion=0.10.0", "-p:IncludeSymbols=false",
            $"-p:RepositoryCommit={sourceGitSha}", "--output", output);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.StandardError + result.StandardOutput);
        }

        return Directory.GetFiles(output, "CP6.Platform.Release.0.10.0.nupkg").Single();
    }

    private static TrustPaths WritePolicy(string root, TestCertificate certificate)
    {
        Directory.CreateDirectory(root);
        var fixture = FormalCertificateTestData.CreatePolicy(certificate);
        var certificateDirectory = Path.Combine(root, "certificates");
        Directory.CreateDirectory(certificateDirectory);
        foreach (var item in fixture.Certificates)
        {
            File.WriteAllBytes(Path.Combine(root, item.Key.Replace('/', Path.DirectorySeparatorChar)), item.Value.ToArray());
        }

        var policyPath = Path.Combine(root, "p10-formal-nuget-trust-store.v1.json");
        File.WriteAllBytes(policyPath, fixture.CanonicalBytes());
        return new(policyPath, certificateDirectory);
    }

    private static SigningCertificate CreateSigningCertificate(string subject)
    {
        const string password = "synthetic-test-password";
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.3") }, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 9, 1, 0, 5, 0, TimeSpan.Zero));
        var der = certificate.Export(X509ContentType.Cert);
        using var publicCertificate = new X509Certificate2(der);
        using var publicKey = publicCertificate.GetRSAPublicKey()!;
        var testCertificate = new TestCertificate(
            der,
            Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant(),
            "sha256:" + Convert.ToHexString(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant(),
            publicCertificate.SubjectName.Name!,
            publicCertificate.IssuerName.Name!,
            new DateTimeOffset(publicCertificate.NotBefore.ToUniversalTime()),
            new DateTimeOffset(publicCertificate.NotAfter.ToUniversalTime()));
        return new(certificate.Export(X509ContentType.Pfx, password), password, testCertificate);
    }

    private static ProcessResult RunTool(params string[] arguments)
    {
        var tool = Path.Combine(
            ReleaseTestData.RepositoryRoot,
            "tools", "CP6.Platform.ReleaseTool", "bin", "Release", "net8.0", "CP6.Platform.ReleaseTool.dll");
        return Run("dotnet", [tool, .. arguments]);
    }

    private static ProcessResult Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = ReleaseTestData.RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} timed out.");
        }

        return new(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private sealed record SigningCertificate(byte[] Pfx, string Password, TestCertificate PublicCertificate);
    private sealed record TrustPaths(string PolicyPath, string CertificateDirectory);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class UnitDirectory : IDisposable
    {
        public UnitDirectory()
        {
            Path = System.IO.Path.Combine(
                ReleaseTestData.RepositoryRoot,
                "artifacts", "p10-formal", "unit", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

file static class FormalPackageVerifierTestArgumentExtensions
{
    public static string[] WithArgument(this string[] values, int index, string replacement)
    {
        var clone = (string[])values.Clone();
        clone[index] = replacement;
        return clone;
    }
}
