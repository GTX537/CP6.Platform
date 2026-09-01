using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CP6.Platform.DeploymentTests;

public sealed class P09PublicationWorkflowTests
{
    private static readonly string RepositoryRoot = P09ContractTestData.RepositoryRoot;

    [Fact]
    public void Workflow_FreezesExactMainSinglePackageTransaction()
    {
        var workflow = ReadRequired(".github/workflows/publish-p09.yml");
        foreach (var required in new[]
        {
            "name: publish-p09-deployment",
            "workflow_dispatch:",
            "expected_commit:",
            "contents: read",
            "packages: write",
            "refs/heads/main",
            "git rev-parse origin/main",
            "./eng/verify.ps1 -Gate Format -Profile ci",
            "./eng/verify.ps1 -Gate Build -Profile ci",
            "./eng/verify.ps1 -Gate Unit -Profile ci",
            "./eng/verify.ps1 -Gate Integration -Profile p05-real",
            "./eng/verify.ps1 -Gate Integration -Profile p06-real",
            "./eng/verify.ps1 -Gate E2E -Profile ci",
            "./eng/verify.ps1 -Gate Contract -Profile ci",
            "./eng/verify.ps1 -Gate Security -Profile ci",
            "./eng/verify.ps1 -P09Real -Profile ci -ExpectedGitSha ${{ inputs.expected_commit }}",
            "./eng/pack-p09.ps1 -VerifyReproducible",
            "CP6.Platform.Deployment.0.9.0-alpha.1.nupkg",
            "https://nuget.pkg.github.com/GTX537/index.json",
            "artifacts/p09-publication/availability-started.json",
            "gh api repos/GTX537/CP6.Platform/git/ref/heads/main --jq .object.sha"
        })
        {
            Assert.Contains(required, workflow, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(workflow, @"dotnet\s+nuget\s+push", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.DoesNotContain("--skip-duplicate", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifacts/p09-package/*.nupkg", workflow, StringComparison.OrdinalIgnoreCase);

        var availabilityChecks = Regex.Matches(workflow, "-Mode Available", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Index)
            .ToArray();
        var manifest = workflow.IndexOf("New-P09PublicationManifest.ps1", StringComparison.Ordinal);
        var push = workflow.IndexOf("dotnet nuget push", StringComparison.Ordinal);
        var published = workflow.IndexOf("-Mode Published", StringComparison.Ordinal);
        Assert.Collection(availabilityChecks, _ => { }, _ => { });
        Assert.True(availabilityChecks[0] < manifest, "Initial Registry availability must precede the candidate manifest.");
        Assert.True(manifest < push, "The candidate manifest must precede mutation.");
        Assert.True(
            manifest < availabilityChecks[1] && availabilityChecks[1] < push,
            "Registry availability must be reconfirmed immediately before mutation.");
        Assert.True(push < published, "Independent Registry verification must follow mutation.");
    }

    [Fact]
    public void Workflow_PreservesCompleteEvidenceAndBoundsCredentials()
    {
        var workflow = ReadRequired(".github/workflows/publish-p09.yml");
        var marker = "      - name: Preserve P09 publication evidence";
        var start = workflow.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The P09 publication evidence upload step is missing.");
        var upload = workflow[start..];
        var paths = upload.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("artifacts/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            [
                "artifacts/p09-package/**",
                "artifacts/p09-publication/**",
                "artifacts/verify/**",
                "artifacts/p05-integration/**",
                "artifacts/p06-sql-integration/**",
                "artifacts/p09-rehearsal/**",
                "artifacts/p09-kubernetes/**"
            ],
            paths);
        foreach (var required in new[]
        {
            "persist-credentials: false",
            "if: always()",
            "name: p09-publication-${{ inputs.expected_commit }}",
            "if-no-files-found: error",
            "retention-days: 30"
        })
        {
            Assert.Contains(required, workflow, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("secrets.", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment:", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker login", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kubectl apply", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Collection(
            Regex.Matches(workflow, @"GITHUB_TOKEN:\s*\$\{\{\s*github\.token\s*\}\}").Cast<Match>(),
            _ => { },
            _ => { },
            _ => { },
            _ => { });
        Assert.Single(
            Regex.Matches(workflow, @"NUGET_AUTH_TOKEN:\s*\$\{\{\s*github\.token\s*\}\}").Cast<Match>());
    }

    [Fact]
    public void PublicationScripts_FreezeCandidateRegistryAndNoRewriteBoundary()
    {
        var manifest = ReadRequired("eng/p09/New-P09PublicationManifest.ps1");
        var registry = ReadRequired("eng/p09/Test-P09RegistryPackage.ps1");
        var combined = manifest + "\n" + registry;

        foreach (var required in new[]
        {
            "CP6.Platform.Deployment",
            "0.9.0-alpha.1",
            "https://nuget.pkg.github.com/GTX537/index.json",
            "https://api.github.com/users/GTX537/packages/nuget/CP6.Platform.Deployment/versions",
            "Available",
            "Published",
            "packageSha256",
            "registryVersionId"
        })
        {
            Assert.Contains(required, combined, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--skip-duplicate", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nuget.org", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet pack", registry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet build", registry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete", registry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unlist", registry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestBuilder_EmitsExactCandidateFromPassedEvidence()
    {
        var sourceSha = new string('a', 40);
        var fixture = CreateManifestFixture(sourceSha);
        try
        {
            var result = RunPowerShell(
                "eng/p09/New-P09PublicationManifest.ps1",
                [
                    "-SourceGitSha", sourceSha,
                    "-WorkflowRunId", "123456",
                    "-WorkflowRunAttempt", "2",
                    "-WorkflowJob", "publish",
                    "-PackageDirectory", fixture.PackageDirectory,
                    "-VerificationDirectory", fixture.VerificationDirectory,
                    "-P05ResultPath", fixture.P05Result,
                    "-P06ResultPath", fixture.P06Result,
                    "-RehearsalDirectory", fixture.RehearsalDirectory,
                    "-KubernetesDirectory", fixture.KubernetesDirectory,
                    "-OutputPath", fixture.OutputPath
                ]);

            Assert.True(result.ExitCode == 0, result.Combined);
            using var document = JsonDocument.Parse(File.ReadAllText(fixture.OutputPath));
            var root = document.RootElement;
            Assert.Equal("Candidate", root.GetProperty("status").GetString());
            Assert.Equal(sourceSha, root.GetProperty("source").GetProperty("gitSha").GetString());
            Assert.Equal("123456", root.GetProperty("source").GetProperty("workflowRunId").GetString());
            Assert.Equal("CP6.Platform.Deployment", root.GetProperty("package").GetProperty("id").GetString());
            Assert.Equal("0.9.0-alpha.1", root.GetProperty("package").GetProperty("version").GetString());
            Assert.Matches(
                new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant),
                root.GetProperty("package").GetProperty("sha256").GetString()!);
            Assert.Equal(11, root.GetProperty("gates").GetArrayLength());
            Assert.Equal("GitHub Packages", root.GetProperty("registry").GetProperty("authority").GetString());
            Assert.DoesNotContain(RepositoryRoot, File.ReadAllText(fixture.OutputPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public void ManifestBuilder_RejectsEvidenceFromAnotherCommit()
    {
        var fixture = CreateManifestFixture(new string('b', 40));
        try
        {
            var result = RunPowerShell(
                "eng/p09/New-P09PublicationManifest.ps1",
                [
                    "-SourceGitSha", new string('a', 40),
                    "-WorkflowRunId", "123456",
                    "-WorkflowRunAttempt", "1",
                    "-PackageDirectory", fixture.PackageDirectory,
                    "-VerificationDirectory", fixture.VerificationDirectory,
                    "-P05ResultPath", fixture.P05Result,
                    "-P06ResultPath", fixture.P06Result,
                    "-RehearsalDirectory", fixture.RehearsalDirectory,
                    "-KubernetesDirectory", fixture.KubernetesDirectory,
                    "-OutputPath", fixture.OutputPath
                ]);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("source SHA does not match", result.Combined, StringComparison.Ordinal);
            Assert.False(File.Exists(fixture.OutputPath));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public void RegistryVerifier_AvailabilityRejectsCollisionWithoutLeakingCredential()
    {
        const string credential = "obvious-fake-registry-credential";
        using (var available = new OneShotHttpServer("[]"))
        {
            var result = RunPowerShell(
                "eng/p09/Test-P09RegistryPackage.ps1",
                ["-Mode", "Available", "-RegistryApiUrl", available.Uri + "versions"],
                new Dictionary<string, string> { ["GITHUB_TOKEN"] = credential });
            Assert.True(result.ExitCode == 0, result.Combined);
            Assert.Contains("Available", result.Combined, StringComparison.Ordinal);
            Assert.DoesNotContain(credential, result.Combined, StringComparison.Ordinal);
        }

        using (var collision = new OneShotHttpServer("[{\"id\":321,\"name\":\"0.9.0-alpha.1\",\"created_at\":\"2026-09-01T00:00:00Z\",\"updated_at\":\"2026-09-01T00:00:00Z\"}]"))
        {
            var result = RunPowerShell(
                "eng/p09/Test-P09RegistryPackage.ps1",
                ["-Mode", "Available", "-RegistryApiUrl", collision.Uri + "versions"],
                new Dictionary<string, string> { ["GITHUB_TOKEN"] = credential });
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal(1, collision.CompletedRequests);
            var collisionOutput = PlainTerminalText(result.Combined);
            Assert.True(
                collisionOutput.Contains("already exists", StringComparison.Ordinal),
                collisionOutput);
            Assert.DoesNotContain(credential, result.Combined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PublicationTestHarness_NormalizesWrappedPowerShellErrors()
    {
        var rendered = "\u001b[31;1malready\r\nexists.\u001b[0m";

        Assert.Equal("already exists.", PlainTerminalText(rendered));
    }

    [Fact]
    public void RegistryVerifier_PublishedModeDownloadsAndMatchesExactPackageBytes()
    {
        const string credential = "obvious-fake-registry-credential";
        var root = Path.Combine(RepositoryRoot, "artifacts", $"p09-registry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var packageBytes = BuildValidDeploymentPackage(root);
            var packageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes))
                .ToLowerInvariant();
            using var server = new RoutedHttpServer(3, (path, baseUri) => path switch
            {
                var value when value.StartsWith("/versions?", StringComparison.Ordinal) =>
                    HttpResponse.Json("[{\"id\":654,\"name\":\"0.9.0-alpha.1\",\"created_at\":\"2026-09-01T00:00:00Z\",\"updated_at\":\"2026-09-01T00:00:00Z\"}]"),
                "/index.json" => HttpResponse.Json(
                    $"{{\"resources\":[{{\"@type\":\"PackageBaseAddress/3.0.0\",\"@id\":\"{baseUri}download\"}}]}}"),
                "/download/cp6.platform.deployment/0.9.0-alpha.1/cp6.platform.deployment.0.9.0-alpha.1.nupkg" =>
                    new HttpResponse("application/octet-stream", packageBytes),
                _ => new HttpResponse("text/plain", Encoding.UTF8.GetBytes("unexpected"), 404)
            });

            var source = server.Uri + "index.json";
            var candidatePath = Path.Combine(root, "candidate.json");
            var outputPath = Path.Combine(root, "registry-result.json");
            var downloadDirectory = Path.Combine(root, "download");
            File.WriteAllText(
                candidatePath,
                JsonSerializer.Serialize(new
                {
                    status = "Candidate",
                    package = new
                    {
                        id = "CP6.Platform.Deployment",
                        version = "0.9.0-alpha.1",
                        file = "CP6.Platform.Deployment.0.9.0-alpha.1.nupkg",
                        sha256 = packageHash
                    },
                    registry = new { source }
                }));

            var result = RunPowerShell(
                "eng/p09/Test-P09RegistryPackage.ps1",
                [
                    "-Mode", "Published",
                    "-RegistryApiUrl", server.Uri + "versions",
                    "-RegistrySource", source,
                    "-CandidateManifestPath", candidatePath,
                    "-OutputPath", outputPath,
                    "-DownloadDirectory", downloadDirectory
                ],
                new Dictionary<string, string> { ["GITHUB_TOKEN"] = credential });

            Assert.True(result.ExitCode == 0, result.Combined);
            Assert.Equal(3, server.CompletedRequests);
            Assert.DoesNotContain(credential, result.Combined, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            Assert.Equal("Verified", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("654", document.RootElement.GetProperty("registryVersionId").GetString());
            Assert.Equal(packageHash, document.RootElement.GetProperty("packageSha256").GetString());
            Assert.Equal("Passed", document.RootElement.GetProperty("packageContent").GetString());
            Assert.Equal(
                packageBytes,
                File.ReadAllBytes(Path.Combine(
                    downloadDirectory,
                    "CP6.Platform.Deployment.0.9.0-alpha.1.nupkg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void P06RealRunner_UsesTheVerifiedDotNetHost()
    {
        var runner = ReadRequired("eng/run-p06-sql-integration.ps1");

        Assert.Contains("$env:DOTNET_HOST_PATH", runner, StringComparison.Ordinal);
        Assert.Contains("Get-Command -Name 'dotnet' -CommandType Application", runner, StringComparison.Ordinal);
        Assert.Contains("& $dotnetCommand run --project", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("& dotnet run --project", runner, StringComparison.Ordinal);
    }

    private static string ReadRequired(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Required P09 publication file is missing: {relativePath}");
        return File.ReadAllText(path);
    }

    private static ManifestFixture CreateManifestFixture(string sourceSha)
    {
        var root = Path.Combine(RepositoryRoot, "artifacts", $"p09-publication-test-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(root, "package");
        var verificationDirectory = Path.Combine(root, "verify");
        var p05Directory = Path.Combine(root, "p05");
        var p06Directory = Path.Combine(root, "p06");
        var rehearsalDirectory = Path.Combine(root, "rehearsal");
        var kubernetesDirectory = Path.Combine(root, "kubernetes");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(p05Directory);
        Directory.CreateDirectory(p06Directory);
        Directory.CreateDirectory(rehearsalDirectory);
        Directory.CreateDirectory(kubernetesDirectory);

        File.WriteAllBytes(
            Path.Combine(packageDirectory, "CP6.Platform.Deployment.0.9.0-alpha.1.nupkg"),
            [1, 2, 3, 4]);
        File.WriteAllBytes(
            Path.Combine(packageDirectory, "CP6.Platform.Deployment.0.9.0-alpha.1.snupkg"),
            [5, 6, 7]);

        foreach (var gateDirectory in new[]
                 {
                     "format", "build", "unit", "p05-real", "p06-real", "e2e", "contract", "security", "p09real"
                 })
        {
            var directory = Path.Combine(verificationDirectory, gateDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "summary.json"),
                JsonSerializer.Serialize(new { status = "Passed", commitSha = sourceSha }));
        }

        var p05Result = Path.Combine(p05Directory, "result.json");
        var p06Result = Path.Combine(p06Directory, "result.json");
        File.WriteAllText(p05Result, "{\"status\":\"Passed\"}");
        File.WriteAllText(p06Result, "{\"status\":\"Passed\"}");

        var evidence = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "contracts",
            "p09",
            "examples",
            "rehearsal-evidence.valid.json")))!.AsObject();
        evidence["platformGitSha"] = sourceSha;
        var rehearsalPath = Path.Combine(rehearsalDirectory, "rehearsal-evidence.v1.json");
        File.WriteAllText(rehearsalPath, evidence.ToJsonString());

        File.WriteAllText(
            Path.Combine(kubernetesDirectory, "kubernetes-contract-result.v1.json"),
            JsonSerializer.Serialize(new
            {
                status = "Passed",
                renderedManifestSha256 = evidence["kubernetesManifestSha256"]!.GetValue<string>()
            }));

        return new ManifestFixture(
            root,
            packageDirectory,
            verificationDirectory,
            p05Result,
            p06Result,
            rehearsalDirectory,
            kubernetesDirectory,
            Path.Combine(root, "candidate-manifest.v1.json"));
    }

    private static ProcessResult RunPowerShell(
        string relativeScript,
        IReadOnlyCollection<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot, relativeScript.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "PowerShell publication contract timed out.");
        return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static string PlainTerminalText(string value)
    {
        var withoutAnsi = Regex.Replace(
            value,
            "\\u001B\\[[0-?]*[ -/]*[@-~]",
            string.Empty,
            RegexOptions.CultureInvariant);
        return Regex.Replace(withoutAnsi, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static byte[] BuildValidDeploymentPackage(string root)
    {
        var output = Path.Combine(root, "pack");
        Directory.CreateDirectory(output);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "pack",
                     "src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj",
                     "--configuration", "Release",
                     "--no-build", "--no-restore",
                     "--output", output,
                     "-p:PackageVersion=0.9.0-alpha.1",
                     "-p:IncludeSymbols=false"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "Deployment package fixture timed out.");
        Assert.True(
            process.ExitCode == 0,
            stdout.GetAwaiter().GetResult() + Environment.NewLine + stderr.GetAwaiter().GetResult());
        var package = Directory.GetFiles(output, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Single(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase));
        return File.ReadAllBytes(package);
    }

    private sealed class OneShotHttpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Task completion;
        private int completedRequests;

        public OneShotHttpServer(string responseBody)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Uri = $"http://127.0.0.1:{endpoint.Port}/";
            completion = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var body = Encoding.UTF8.GetBytes(responseBody);
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers);
                await stream.WriteAsync(body);
                Interlocked.Increment(ref completedRequests);
            });
        }

        public string Uri { get; }

        public int CompletedRequests => Volatile.Read(ref completedRequests);

        public void Dispose()
        {
            listener.Stop();
            completion.GetAwaiter().GetResult();
        }
    }

    private sealed class RoutedHttpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Task completion;
        private int completedRequests;

        public RoutedHttpServer(int expectedRequests, Func<string, string, HttpResponse> responder)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Uri = $"http://127.0.0.1:{endpoint.Port}/";
            completion = Task.Run(async () =>
            {
                for (var requestIndex = 0; requestIndex < expectedRequests; requestIndex++)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    await using var stream = client.GetStream();
                    var buffer = new byte[4096];
                    var request = new StringBuilder();
                    while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer);
                        if (read == 0)
                        {
                            break;
                        }
                        request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }
                    var requestLine = request.ToString().Split("\r\n", StringSplitOptions.None)[0];
                    var path = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    var response = responder(path, Uri);
                    var reason = response.StatusCode == 200 ? "OK" : "Not Found";
                    var headers = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {response.StatusCode} {reason}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {response.Body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers);
                    await stream.WriteAsync(response.Body);
                    Interlocked.Increment(ref completedRequests);
                }
            });
        }

        public string Uri { get; }

        public int CompletedRequests => Volatile.Read(ref completedRequests);

        public void Dispose()
        {
            listener.Stop();
            completion.GetAwaiter().GetResult();
        }
    }

    private sealed record HttpResponse(string ContentType, byte[] Body, int StatusCode = 200)
    {
        public static HttpResponse Json(string value) =>
            new("application/json", Encoding.UTF8.GetBytes(value));
    }

    private sealed record ManifestFixture(
        string Root,
        string PackageDirectory,
        string VerificationDirectory,
        string P05Result,
        string P06Result,
        string RehearsalDirectory,
        string KubernetesDirectory,
        string OutputPath);

    private sealed record ProcessResult(int ExitCode, string Output, string Error)
    {
        public string Combined => Output + Environment.NewLine + Error;
    }
}
