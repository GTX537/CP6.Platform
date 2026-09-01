using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace CP6.Platform.ReleaseTests;

internal static class P10PackageTestHarness
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    public static string[] PackReleasePackage(string version)
    {
        var output = CreateUnitDirectory();
        try
        {
            Run(
                "dotnet",
                "pack",
                "src/CP6.Platform.Release/CP6.Platform.Release.csproj",
                "--configuration",
                "Release",
                "--no-restore",
                $"-p:PackageVersion={version}",
                "-p:IncludeSymbols=false",
                "--output",
                output);

            var package = Directory.GetFiles(output, "*.nupkg", SearchOption.TopDirectoryOnly).Single();
            using var archive = ZipFile.OpenRead(package);
            return archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
        }
        finally
        {
            DeleteDirectory(output);
        }
    }

    public static JsonDocument BuildTestSetForCurrentCommit()
    {
        var output = CreateUnitDirectory();
        try
        {
            var sourceGitSha = Capture("git", "rev-parse", "HEAD").Trim();
            if (sourceGitSha.Length != 40 || sourceGitSha.Any(character => character is < '0' or > '9' and < 'a' or > 'f'))
            {
                throw new InvalidOperationException("Current Git SHA is not a lowercase 40-character hexadecimal value.");
            }

            Run(
                "pwsh",
                "-NoProfile",
                "-File",
                "eng/p10/New-P10TestPackageSet.ps1",
                "-SourceGitSha",
                sourceGitSha,
                "-RunId",
                "1",
                "-RunAttempt",
                "1",
                "-OutputPath",
                output);

            var certificatePath = Path.Combine(output, "test-signing-public.cer");
            var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(certificatePath))).ToLowerInvariant();
            Run(
                "pwsh",
                "-NoProfile",
                "-File",
                "eng/p10/Test-P10TestPackageSet.ps1",
                "-PackagePath",
                output,
                "-ExpectedSourceGitSha",
                sourceGitSha,
                "-ExpectedRunId",
                "1",
                "-ExpectedRunAttempt",
                "1",
                "-ExpectedCertificateFingerprint",
                fingerprint);

            return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "test-package-manifest.v1.json")));
        }
        finally
        {
            DeleteDirectory(output);
        }
    }

    public static void AssertInjectedFailureCleansPrivateMaterial()
    {
        var output = CreateUnitDirectory();
        var privateRoot = Path.Combine(ReleaseTestData.RepositoryRoot, "artifacts", "p10-test", "private");
        var privatePfxBefore = Directory.Exists(privateRoot)
            ? Directory.GetFiles(privateRoot, "*.pfx", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray()
            : [];
        var trustedBefore = CountTestCertificates();
        try
        {
            var sourceGitSha = Capture("git", "rev-parse", "HEAD").Trim();
            var result = RunProcess(
                "pwsh",
                "-NoProfile",
                "-File",
                "eng/p10/New-P10TestPackageSet.ps1",
                "-SourceGitSha",
                sourceGitSha,
                "-RunId",
                "1",
                "-RunAttempt",
                "1",
                "-OutputPath",
                output,
                "-InjectFailureAfterSigning");
            if (result.ExitCode == 0)
            {
                throw new InvalidOperationException("Injected signing failure unexpectedly succeeded.");
            }

            var privatePfxAfter = Directory.Exists(privateRoot)
                ? Directory.GetFiles(privateRoot, "*.pfx", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray()
                : [];
            if (!privatePfxBefore.SequenceEqual(privatePfxAfter, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Injected signing failure left PFX material under artifacts/p10-test/private.");
            }
            if (trustedBefore != CountTestCertificates())
            {
                throw new InvalidOperationException("Injected signing failure left a test certificate in CurrentUser/Root.");
            }
        }
        finally
        {
            DeleteDirectory(output);
        }
    }

    private static string CreateUnitDirectory()
    {
        var root = Path.Combine(ReleaseTestData.RepositoryRoot, "artifacts", "p10-test", "unit");
        var output = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        return output;
    }

    private static string Capture(string fileName, params string[] arguments) => Run(fileName, arguments);

    private static string Run(string fileName, params string[] arguments)
    {
        var result = RunProcess(fileName, arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {result.ExitCode}. stdout: {result.StandardOutput} stderr: {result.StandardError}");
        }

        return result.StandardOutput;
    }

    private static ProcessResult RunProcess(string fileName, params string[] arguments)
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
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"{fileName} timed out. stdout: {standardOutput.GetAwaiter().GetResult()} stderr: {standardError.GetAwaiter().GetResult()}");
        }

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();
        return new(process.ExitCode, output, error);
    }

    private static int CountTestCertificates()
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Count(certificate =>
            string.Equals(certificate.Subject, "CN=CP6 Platform P10 TEST ONLY", StringComparison.Ordinal));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
