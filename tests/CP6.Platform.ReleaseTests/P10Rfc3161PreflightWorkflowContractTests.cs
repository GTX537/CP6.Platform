namespace CP6.Platform.ReleaseTests;

public sealed class P10Rfc3161PreflightWorkflowContractTests
{
    private const string CheckoutPin = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
    private const string SetupDotNetPin = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";
    private const string UploadArtifactPin = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a";
    private const string DownloadArtifactPin = "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c";

    private static readonly string WorkflowPath = Path.Combine(
        ReleaseTestData.RepositoryRoot,
        ".github",
        "workflows",
        "p10-rfc3161-preflight.yml");

    [Fact]
    public void Preflight_is_manual_or_pull_request_two_runner_and_read_only()
    {
        Assert.True(File.Exists(WorkflowPath), "P10 RFC3161 preflight workflow is missing.");
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("workflow_dispatch:", text, StringComparison.Ordinal);
        Assert.Contains("pull_request:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target:", text, StringComparison.Ordinal);
        Assert.Contains("permissions:\n  contents: read", Normalize(text), StringComparison.Ordinal);
        Assert.Contains("runner: [windows-2025, ubuntu-latest]", text, StringComparison.Ordinal);
        Assert.Contains("runs-on: ${{ matrix.runner }}", text, StringComparison.Ordinal);
        Assert.Contains("needs: probe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("packages: write", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preflight_uses_the_real_probe_and_compares_public_identity()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("probe-rfc3161", text, StringComparison.Ordinal);
        Assert.Contains("http://timestamp.digicert.com", text, StringComparison.Ordinal);
        Assert.Contains("policyOid", text, StringComparison.Ordinal);
        Assert.Contains("certificateChainSha256", text, StringComparison.Ordinal);
        Assert.Contains("p10-rfc3161-windows-2025", text, StringComparison.Ordinal);
        Assert.Contains("p10-rfc3161-ubuntu-latest", text, StringComparison.Ordinal);
        Assert.Contains("p10-rfc3161-two-runner-evidence", text, StringComparison.Ordinal);
        Assert.Contains("Policy OIDs differ", text, StringComparison.Ordinal);
        Assert.Contains("certificate chains differ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_uses_only_approved_action_pins_and_has_no_release_path()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.Equal(1, Count(text, CheckoutPin));
        Assert.Equal(1, Count(text, SetupDotNetPin));
        Assert.Equal(2, Count(text, UploadArtifactPin));
        Assert.Equal(1, Count(text, DownloadArtifactPin));
        Assert.DoesNotContain("uses: actions/checkout@v", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses: actions/setup-dotnet@v", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses: actions/upload-artifact@v", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses: actions/download-artifact@v", text, StringComparison.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "nuget push",
            "dotnet pack",
            "cosign",
            "wrangler",
            "aws s3",
            "rclone",
            "docker",
            "kubectl",
            "deploy"
        ];
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GitHub_context_values_are_not_interpolated_into_powershell_source()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.DoesNotContain("${{", ExtractRunScripts(text), StringComparison.Ordinal);
        Assert.Contains("P10_RUNNER_IMAGE: ${{ matrix.runner }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_RUN_ID: ${{ github.run_id }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_RUN_ATTEMPT: ${{ github.run_attempt }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_SOURCE_SHA: ${{ github.sha }}", text, StringComparison.Ordinal);
    }

    private static string ExtractRunScripts(string text)
    {
        var lines = Normalize(text).Split('\n');
        var scripts = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var marker = lines[index];
            if (!string.Equals(marker.Trim(), "run: |", StringComparison.Ordinal))
            {
                continue;
            }

            var markerIndent = marker.Length - marker.TrimStart().Length;
            for (index++; index < lines.Length; index++)
            {
                var line = lines[index];
                var indent = line.Length - line.TrimStart().Length;
                if (line.Length != 0 && indent <= markerIndent)
                {
                    index--;
                    break;
                }

                scripts.Add(line);
            }
        }

        return string.Join('\n', scripts);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int Count(string value, string fragment)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0; index += fragment.Length)
        {
            count++;
        }
        return count;
    }
}
