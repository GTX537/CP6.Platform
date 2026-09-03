namespace CP6.Platform.ReleaseTests;

public sealed class P10SignerRevalidationWorkflowContractTests
{
    private const string CheckoutPin = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
    private const string SetupDotNetPin = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";

    private static readonly string WorkflowPath = Path.Combine(
        ReleaseTestData.RepositoryRoot,
        ".github",
        "workflows",
        "p10-formal-signer-revalidation.yml");

    [Fact]
    public void Revalidation_is_manual_exact_main_and_environment_protected()
    {
        Assert.True(File.Exists(WorkflowPath), "P10 formal signer revalidation workflow is missing.");
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("workflow_dispatch:", text, StringComparison.Ordinal);
        Assert.Contains("expected_commit:", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "required: true"));
        Assert.Contains("environment: p10-formal-release", text, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-2025", text, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ github.sha }}", text, StringComparison.Ordinal);
        Assert.Contains("if ($env:P10_EVENT_REF -cne 'refs/heads/main')", text, StringComparison.Ordinal);
        Assert.Contains("if ($env:P10_EVENT_SHA -cne $env:P10_EXPECTED_COMMIT)", text, StringComparison.Ordinal);
        Assert.Contains("if ((git rev-parse HEAD).Trim() -cne $env:P10_EXPECTED_COMMIT)", text, StringComparison.Ordinal);
        Assert.Contains("/git/ref/heads/main", text, StringComparison.Ordinal);
        Assert.Contains("if ($remoteMain -cne $env:P10_EXPECTED_COMMIT)", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, CheckoutPin));
        Assert.Equal(1, Count(text, SetupDotNetPin));
    }

    [Fact]
    public void Revalidation_binds_only_the_protected_signing_secrets_and_merged_public_trust()
    {
        var text = File.ReadAllText(WorkflowPath);
        const string pfxSecret = "secrets.P10_NUGET_SIGNING_PFX_BASE64";
        const string passwordSecret = "secrets.P10_NUGET_SIGNING_PFX_PASSWORD";

        Assert.Equal(1, Count(text, pfxSecret));
        Assert.Equal(1, Count(text, passwordSecret));
        Assert.Contains("./eng/p10/Test-P10FormalSignerIdentity.ps1", text, StringComparison.Ordinal);
        Assert.Contains("if ($identity.Status -cne 'Success')", text, StringComparison.Ordinal);
        Assert.DoesNotContain("S04_EXTERNAL_PREREQUISITES_READY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("P10_CRM_TRUST_POLICY_SHA256", text, StringComparison.Ordinal);
        Assert.DoesNotContain("P10_SYSTEM_TRUST_POLICY_SHA256", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Revalidation_is_read_only_and_has_no_publication_or_artifact_path()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("permissions:\n      contents: read", Normalize(text), StringComparison.Ordinal);
        Assert.DoesNotContain("packages: read", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packages: write", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id-token: write", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upload-artifact", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download-artifact", text, StringComparison.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "dotnet pack",
            "nuget push",
            "New-P10FormalPackageSet",
            "Publish-P10FormalPackageSet",
            "wrangler",
            "aws s3",
            "rclone",
            "cosign",
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
        Assert.Contains("P10_EXPECTED_COMMIT: ${{ inputs.expected_commit }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_EVENT_SHA: ${{ github.sha }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_EVENT_REF: ${{ github.ref }}", text, StringComparison.Ordinal);
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
