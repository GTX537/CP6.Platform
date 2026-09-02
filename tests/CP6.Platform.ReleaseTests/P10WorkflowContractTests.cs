namespace CP6.Platform.ReleaseTests;

public sealed class P10WorkflowContractTests
{
    private static readonly string WorkflowPath = Path.Combine(
        ReleaseTestData.RepositoryRoot,
        ".github",
        "workflows",
        "p10-test-candidate.yml");

    [Fact]
    public void S02_workflow_is_exact_main_manual_and_least_privilege()
    {
        Assert.True(File.Exists(WorkflowPath), "P10 S02 workflow is missing.");
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("workflow_dispatch:", text, StringComparison.Ordinal);
        Assert.Contains("expected_commit:", text, StringComparison.Ordinal);
        Assert.Contains("required: true", text, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-latest", text, StringComparison.Ordinal);
        Assert.Contains("if ('${{ github.ref }}' -cne 'refs/heads/main')", text, StringComparison.Ordinal);
        Assert.Contains("if ('${{ github.sha }}' -cne '${{ inputs.expected_commit }}')", text, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ github.sha }}", text, StringComparison.Ordinal);
        Assert.Contains("contents: read", text, StringComparison.Ordinal);
        Assert.Contains("actions: read", text, StringComparison.Ordinal);
        Assert.DoesNotContain("packages: write", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id-token: write", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment:", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void S02_workflow_uses_pinned_actions_and_two_immutable_ninety_day_artifacts()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683", text, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9", text, StringComparison.Ordinal);
        Assert.Equal(2, Count(text, "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02"));
        Assert.Contains("name: p10-s02-packages-${{ inputs.expected_commit }}-${{ github.run_attempt }}", text, StringComparison.Ordinal);
        Assert.Contains("name: p10-s02-transport-${{ inputs.expected_commit }}-${{ github.run_attempt }}", text, StringComparison.Ordinal);
        Assert.Equal(2, Count(text, "overwrite: false"));
        Assert.Equal(2, Count(text, "retention-days: 90"));
        Assert.DoesNotContain("overwrite: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void S02_workflow_verifies_before_upload_and_binds_transport_to_artifact_api_metadata()
    {
        var text = File.ReadAllText(WorkflowPath);
        var verifyIndex = text.IndexOf("./eng/p10/Test-P10TestPackageSet.ps1", StringComparison.Ordinal);
        var packageUploadIndex = text.IndexOf("name: Upload immutable package set", StringComparison.Ordinal);

        Assert.True(verifyIndex >= 0 && packageUploadIndex > verifyIndex, "Independent verification must precede package upload.");
        Assert.Contains("id: packages", text, StringComparison.Ordinal);
        Assert.Contains("steps.packages.outputs.artifact-id", text, StringComparison.Ordinal);
        Assert.Contains("steps.packages.outputs.artifact-digest", text, StringComparison.Ordinal);
        Assert.Contains("gh api repos/${{ github.repository }}/actions/artifacts/${{ steps.packages.outputs.artifact-id }}", text, StringComparison.Ordinal);
        Assert.Contains("[Text.Json.JsonDocument]::Parse(($artifactJson -join \"`n\"))", text, StringComparison.Ordinal);
        Assert.Contains("$artifact.GetProperty('created_at').GetString()", text, StringComparison.Ordinal);
        Assert.Contains("$artifact.GetProperty('expires_at').GetString()", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ConvertFrom-Json", text, StringComparison.Ordinal);
        Assert.Contains("./eng/p10/New-P10TransportRecord.ps1", text, StringComparison.Ordinal);
        Assert.Contains("./eng/p10/Test-P10TransportRecord.ps1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj --configuration Release --no-build -- validate-transport", text, StringComparison.Ordinal);
    }

    [Fact]
    public void S02_workflow_never_publishes_deploys_or_claims_formal_state()
    {
        var text = File.ReadAllText(WorkflowPath);
        string[] forbidden =
        [
            "nuget push",
            "nuget.pkg.github.com",
            "wrangler r2",
            "aws s3",
            "rclone",
            "cosign.key",
            "kubectl apply",
            "docker compose up",
            "formal candidate",
            "production ready"
        ];

        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Common_validation_keeps_contract_gate_on_linux_and_windows()
    {
        var path = Path.Combine(ReleaseTestData.RepositoryRoot, ".github", "workflows", "platform-validation.yml");
        var text = File.ReadAllText(path);

        Assert.Contains("- ubuntu-latest", text, StringComparison.Ordinal);
        Assert.Contains("- windows-latest", text, StringComparison.Ordinal);
        Assert.Contains("./eng/verify.ps1 -Gate Contract -Profile ci", text, StringComparison.Ordinal);

        var verifyPath = Path.Combine(ReleaseTestData.RepositoryRoot, "eng", "verify.ps1");
        var verify = File.ReadAllText(verifyPath);
        var architecture = verify.IndexOf("Invoke-DotNetStep -Name 'Architecture'", StringComparison.Ordinal);
        var releaseContracts = verify.IndexOf("Invoke-DotNetStep -Name 'ReleaseContracts'", StringComparison.Ordinal);
        var scriptContracts = verify.IndexOf("Invoke-PowerShellStep -Name 'P10PackageScriptContracts'", StringComparison.Ordinal);
        var formalScriptContracts = verify.IndexOf("Invoke-PowerShellStep -Name 'P10FormalPackageScriptContracts'", StringComparison.Ordinal);
        var reproducibility = verify.IndexOf("Assert-ReproduciblePackages", releaseContracts, StringComparison.Ordinal);
        Assert.True(
            architecture >= 0 && releaseContracts > architecture && scriptContracts > releaseContracts &&
            formalScriptContracts > scriptContracts && reproducibility > formalScriptContracts,
            "Contract gate must run Architecture, ReleaseContracts, both P10 script contracts, then reproducibility in order.");
    }

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
