namespace CP6.Platform.ReleaseTests;

public sealed class P10FormalWorkflowContractTests
{
    private const string CheckoutPin = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
    private const string SetupDotNetPin = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";
    private const string UploadArtifactPin = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a";
    private const string DownloadArtifactPin = "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c";

    private static readonly string WorkflowPath = Path.Combine(
        ReleaseTestData.RepositoryRoot,
        ".github",
        "workflows",
        "p10-formal-packages.yml");

    private static readonly string RecoveryWorkflowPath = Path.Combine(
        ReleaseTestData.RepositoryRoot,
        ".github",
        "workflows",
        "p10-formal-packages-recovery.yml");

    [Fact]
    public void Formal_workflow_is_manual_exact_main_fixed_version_and_protected()
    {
        Assert.True(File.Exists(WorkflowPath), "P10 S04 formal workflow is missing.");
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("workflow_dispatch:", text, StringComparison.Ordinal);
        Assert.Contains("expected_commit:", text, StringComparison.Ordinal);
        Assert.Contains("version:", text, StringComparison.Ordinal);
        Assert.Equal(2, Count(text, "required: true"));
        Assert.Contains("if ($env:P10_EVENT_REF -cne 'refs/heads/main')", text, StringComparison.Ordinal);
        Assert.Contains("if ($env:P10_EVENT_SHA -cne $env:P10_EXPECTED_COMMIT)", text, StringComparison.Ordinal);
        Assert.Contains("if ($env:P10_PACKAGE_VERSION -cne '0.10.0')", text, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ github.sha }}", text, StringComparison.Ordinal);
        Assert.Contains("sign-publish:\n    runs-on: windows-2025\n    timeout-minutes: 45\n    environment: p10-formal-release\n    permissions:\n      contents: read\n      packages: write", Normalize(text), StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formal_workflow_uses_only_the_approved_node24_action_pins()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.Equal(2, Count(text, CheckoutPin));
        Assert.Equal(2, Count(text, SetupDotNetPin));
        Assert.Equal(2, Count(text, UploadArtifactPin));
        Assert.Equal(1, Count(text, DownloadArtifactPin));
        Assert.DoesNotContain("actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683", text, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9", text, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02", text, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/checkout@v", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses: actions/setup-dotnet@v", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses: actions/upload-artifact@v", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses: actions/download-artifact@v", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secrets_are_exposed_only_to_the_single_signing_and_publication_step()
    {
        var text = File.ReadAllText(WorkflowPath);
        const string pfxSecret = "secrets.P10_NUGET_SIGNING_PFX_BASE64";
        const string passwordSecret = "secrets.P10_NUGET_SIGNING_PFX_PASSWORD";

        Assert.Equal(1, Count(text, pfxSecret));
        Assert.Equal(1, Count(text, passwordSecret));
        var stepIndex = text.IndexOf("name: Preflight, sign, publish, and read back", StringComparison.Ordinal);
        var nextStepIndex = text.IndexOf("\n      - name:", stepIndex + 1, StringComparison.Ordinal);
        Assert.True(stepIndex >= 0 && nextStepIndex > stepIndex, "The protected signing step must have a finite step scope.");
        var protectedStep = text[stepIndex..nextStepIndex];
        Assert.Contains(pfxSecret, protectedStep, StringComparison.Ordinal);
        Assert.Contains(passwordSecret, protectedStep, StringComparison.Ordinal);
        Assert.Contains("-UseProtectedEnvironmentSecretBinding", protectedStep, StringComparison.Ordinal);
        Assert.DoesNotContain(pfxSecret, text.Remove(stepIndex, nextStepIndex - stepIndex), StringComparison.Ordinal);
        Assert.DoesNotContain(passwordSecret, text.Remove(stepIndex, nextStepIndex - stepIndex), StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_verifies_before_publish_and_uploads_only_public_readback_evidence()
    {
        var text = File.ReadAllText(WorkflowPath);
        var newSet = text.IndexOf("./eng/p10/New-P10FormalPackageSet.ps1", StringComparison.Ordinal);
        var publish = text.IndexOf("./eng/p10/Publish-P10FormalPackageSet.ps1", StringComparison.Ordinal);
        var publicScan = text.IndexOf("name: Scan Windows public evidence", StringComparison.Ordinal);
        var upload = text.IndexOf("name: Upload immutable Windows read-back evidence", StringComparison.Ordinal);

        Assert.True(newSet >= 0 && publish > newSet && publicScan > publish && upload > publicScan,
            "Windows must verify while building, publish, scan feed read-back evidence, then upload it.");
        Assert.Contains("artifacts/p10-formal/public-windows", text, StringComparison.Ordinal);
        Assert.Contains("feed-readback-packages", text, StringComparison.Ordinal);
        Assert.Contains("formal-package-readback.v1.json", text, StringComparison.Ordinal);
        Assert.Contains("build-invocation-provenance.v1.json", text, StringComparison.Ordinal);
        Assert.Contains("formal-package-verification.v1.json", text, StringComparison.Ordinal);
        Assert.Contains("p10-formal-nuget-trust-store.v1.json", text, StringComparison.Ordinal);
        Assert.Contains("*.cer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("symbols", ArtifactUploadBlock(text, upload), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("package-set/packages", ArtifactUploadBlock(text, upload), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pfx", ArtifactUploadBlock(text, upload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Linux_independently_verifies_feed_bytes_and_emits_final_immutable_evidence()
    {
        var text = File.ReadAllText(WorkflowPath);
        var linux = text.IndexOf("verify-linux:", StringComparison.Ordinal);
        var download = text.IndexOf(DownloadArtifactPin, linux, StringComparison.Ordinal);
        var verify = text.IndexOf("./eng/p10/Test-P10FormalPackageSet.ps1", linux, StringComparison.Ordinal);
        var finalRecord = text.IndexOf("./eng/p10/New-P10FormalPublicationRecord.ps1", linux, StringComparison.Ordinal);
        var scan = text.IndexOf("name: Scan Linux public evidence", linux, StringComparison.Ordinal);
        var upload = text.IndexOf("name: Upload immutable final publication evidence", linux, StringComparison.Ordinal);

        Assert.Contains("needs: sign-publish", text, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-latest", text, StringComparison.Ordinal);
        Assert.True(linux >= 0 && download > linux && verify > download && finalRecord > verify && scan > finalRecord && upload > scan,
            "Linux must download the feed read-back artifact, verify it, create final evidence, scan, and upload.");
        Assert.Contains("-LinuxVerification Success", text, StringComparison.Ordinal);
        Assert.Contains("validate-formal-publication", text, StringComparison.Ordinal);
        Assert.Equal(2, Count(text, "overwrite: false"));
        Assert.Equal(2, Count(text, "retention-days: 90"));
        Assert.DoesNotContain("overwrite: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formal_workflow_has_unconditional_private_residue_cleanup_and_no_unapproved_release_path()
    {
        var text = File.ReadAllText(WorkflowPath);

        Assert.Contains("name: Remove Windows private material and prove no residue", text, StringComparison.Ordinal);
        Assert.Contains("if: ${{ always() }}", text, StringComparison.Ordinal);
        Assert.Contains("p10-formal-signing-private.pfx", text, StringComparison.Ordinal);
        Assert.Contains("$env:RUNNER_TEMP", text, StringComparison.Ordinal);
        Assert.Contains("artifacts/p10-formal", text, StringComparison.Ordinal);
        Assert.Contains("'.pfx', '.p12', '.key', '.pem'", text, StringComparison.Ordinal);

        string[] forbidden =
        [
            "wrangler r2",
            "aws s3",
            "rclone",
            "cosign",
            "azure",
            "--skip-duplicate",
            "delete-package-version",
            "package delete",
            "nuget delete",
            "kubectl",
            "docker compose"
        ];
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GitHub_context_values_are_never_interpolated_into_powershell_source()
    {
        var text = File.ReadAllText(WorkflowPath);
        var runScripts = ExtractRunScripts(text);

        Assert.DoesNotContain("${{", runScripts, StringComparison.Ordinal);
        Assert.Contains("P10_EXPECTED_COMMIT: ${{ inputs.expected_commit }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_PACKAGE_VERSION: ${{ inputs.version }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_EVENT_SHA: ${{ github.sha }}", text, StringComparison.Ordinal);
        Assert.Contains("P10_EVENT_REF: ${{ github.ref }}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_workflow_is_read_only_and_binds_the_failed_publication_evidence()
    {
        Assert.True(File.Exists(RecoveryWorkflowPath), "P10 S04 formal recovery workflow is missing.");
        var text = File.ReadAllText(RecoveryWorkflowPath);

        Assert.Contains("workflow_dispatch:", text, StringComparison.Ordinal);
        Assert.Contains("publication_run_id:", text, StringComparison.Ordinal);
        Assert.Contains("publication_run_attempt:", text, StringComparison.Ordinal);
        Assert.Contains("publication_commit:", text, StringComparison.Ordinal);
        Assert.Contains("version:", text, StringComparison.Ordinal);
        Assert.Contains("windows_artifact_id:", text, StringComparison.Ordinal);
        Assert.Contains("windows_artifact_digest:", text, StringComparison.Ordinal);
        Assert.Contains("permissions:\n      actions: read\n      contents: read", Normalize(text), StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-latest", text, StringComparison.Ordinal);
        Assert.Contains("repos/$env:GITHUB_REPOSITORY/actions/runs/$env:P10_PUBLICATION_RUN_ID", text, StringComparison.Ordinal);
        Assert.Contains("repos/$env:GITHUB_REPOSITORY/actions/artifacts/$env:P10_WINDOWS_ARTIFACT_ID", text, StringComparison.Ordinal);
        Assert.Contains("run-id: ${{ inputs.publication_run_id }}", text, StringComparison.Ordinal);
        Assert.Contains("github-token: ${{ github.token }}", text, StringComparison.Ordinal);
        Assert.Contains(DownloadArtifactPin, text, StringComparison.Ordinal);
        Assert.Contains("./eng/p10/Test-P10FormalPackageSet.ps1", text, StringComparison.Ordinal);
        Assert.Contains("./eng/p10/New-P10FormalPublicationRecord.ps1", text, StringComparison.Ordinal);
        Assert.Contains("-RunId ([long]$env:P10_PUBLICATION_RUN_ID)", text, StringComparison.Ordinal);
        Assert.Contains("-RunAttempt ([int]$env:P10_PUBLICATION_RUN_ATTEMPT)", text, StringComparison.Ordinal);
        Assert.Contains("name: Upload immutable recovered final publication evidence", text, StringComparison.Ordinal);
        Assert.Contains(UploadArtifactPin, text, StringComparison.Ordinal);

        string[] forbidden =
        [
            "packages: write",
            "p10-formal-release",
            "P10_NUGET_SIGNING_PFX",
            "New-P10FormalPackageSet.ps1",
            "Publish-P10FormalPackageSet.ps1",
            "nuget push",
            "--skip-duplicate",
            "delete-package-version",
            "package delete",
            "nuget delete",
            "wrangler r2",
            "aws s3",
            "kubectl",
            "docker compose"
        ];
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("${{", ExtractRunScripts(text), StringComparison.Ordinal);
    }

    private static string ArtifactUploadBlock(string text, int uploadIndex)
    {
        Assert.True(uploadIndex >= 0, "Windows artifact upload step is missing.");
        var nextStep = text.IndexOf("\n      - name:", uploadIndex + 1, StringComparison.Ordinal);
        return nextStep < 0 ? text[uploadIndex..] : text[uploadIndex..nextStep];
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
