using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Nodes;
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09EvidenceTests
{
    public static IEnumerable<object[]> UnsafeSummaryCorpus =>
        P09ContractTestData.UnsafeSummaries.Select(value => new object[] { value });

    public static IEnumerable<object[]> SafeSummaryCorpus =>
        P09ContractTestData.SafeSummaries.Select(value => new object[] { value });

    public static TheoryData<string, string> UnsafeEvidenceMutations => new()
    {
        { "windows-path", "unsafe-evidence" },
        { "embedded-windows-backslash-path", "unsafe-evidence" },
        { "embedded-windows-forward-slash-path", "unsafe-evidence" },
        { "unc-path", "unsafe-evidence" },
        { "embedded-unc-path", "unsafe-evidence" },
        { "unix-home-path", "unsafe-evidence" },
        { "unix-users-path", "unsafe-evidence" },
        { "unix-temp-path", "unsafe-evidence" },
        { "unix-var-path", "unsafe-evidence" },
        { "unix-etc-path", "unsafe-evidence" },
        { "unix-opt-path", "unsafe-evidence" },
        { "unix-srv-path", "unsafe-evidence" },
        { "unix-workspace-path", "unsafe-evidence" },
        { "colon-unix-path", "unsafe-evidence" },
        { "file-uri-path", "unsafe-evidence" },
        { "forward-unc-path", "unsafe-evidence" },
        { "unicode-unix-path", "unsafe-evidence" },
        { "lookalike-http-path", "unsafe-evidence" },
        { "malformed-http-path", "unsafe-evidence" },
        { "uri-userinfo-password", "unsafe-evidence" },
        { "uri-userinfo-username", "unsafe-evidence" },
        { "uri-userinfo-percent-encoded", "unsafe-evidence" },
        { "password-assignment", "unsafe-evidence" },
        { "password-tab-assignment", "unsafe-evidence" },
        { "password-nbsp-assignment", "unsafe-evidence" },
        { "password-next-line-assignment", "unsafe-evidence" },
        { "token-assignment", "unsafe-evidence" },
        { "connection-string-assignment", "unsafe-evidence" },
        { "bearer-credential", "unsafe-evidence" },
        { "bearer-tab-credential", "unsafe-evidence" },
        { "bearer-nbsp-credential", "unsafe-evidence" },
        { "bearer-next-line-credential", "unsafe-evidence" },
        { "secret-field-password", "unsafe-evidence" },
        { "secret-field-token", "unsafe-evidence" },
        { "secret-field-connection-string", "unsafe-evidence" },
        { "secret-field-secret-value", "unsafe-evidence" }
    };

    public static TheoryData<string, string> InvalidShapeMutations => new()
    {
        { "missing-root", "missing-property" },
        { "unknown-root", "unknown-property" },
        { "wrong-root-type", "wrong-type" },
        { "wrong-nested-type", "wrong-type" },
        { "uppercase-hash", "invalid-hash" },
        { "short-hash", "invalid-hash" },
        { "uppercase-git-sha", "invalid-hash" },
        { "wrong-profile-id", "profile-id" },
        { "duplicate-check", "duplicate-check" },
        { "missing-check", "required-checks" },
        { "reordered-check", "required-checks" },
        { "non-utc-start", "invalid-time" },
        { "invalid-time", "invalid-time" },
        { "completed-before-start", "invalid-time" },
        { "trace-publisher-equals-receiver", "trace-span" },
        { "trace-invoke-parent-equals-child", "trace-span" },
        { "zero-trace-id", "trace" },
        { "zero-invocation-trace-id", "trace" },
        { "zero-publisher-span-id", "trace" },
        { "zero-receiver-span-id", "trace" },
        { "zero-invoker-span-id", "trace" },
        { "zero-invoked-span-id", "trace" },
        { "negative-teardown-count", "invalid-count" },
        { "empty-summary", "invalid-string" },
        { "long-summary", "unsafe-evidence" },
        { "multiline-summary", "unsafe-evidence" },
        { "invalid-check-id", "invalid-check" },
        { "too-precise-timestamp", "invalid-time" },
        { "oversized-teardown-count", "wrong-type" }
    };

    public static TheoryData<string> FalsePassMutations => new()
    {
        "failed-check",
        "teardown-exit",
        "teardown-container",
        "teardown-network",
        "teardown-volume",
        "teardown-image",
        "teardown-directory"
    };

    public static TheoryData<string> TeardownNumericProperties => new()
    {
        "commandExitCode",
        "containerCount",
        "networkCount",
        "volumeCount",
        "imageCount"
    };

    [Fact]
    public void Parse_ValidCanonicalEvidence_ExposesImmutableViewAndValidatesAgainstProfile()
    {
        var profile = Cp6P09RuntimeProfile.Parse(
            P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json"));
        var json = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");

        var evidence = Cp6P09RehearsalEvidence.Parse(json);
        evidence.ValidateAgainst(profile);
        var fromUtf8 = Cp6P09RehearsalEvidence.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal("1", evidence.SchemaVersion);
        Assert.Equal(Cp6P09RuntimeProfile.ExpectedProfileId, evidence.ProfileId);
        Assert.Equal(profile.Sha256, evidence.ProfileSha256);
        Assert.Equal(new string('c', 40), evidence.PlatformGitSha);
        Assert.Equal("0.9.0.0", evidence.RepositoryVersion);
        Assert.Equal("0.9.0-alpha.1", evidence.PackageVersion);
        Assert.Equal("Passed", evidence.Overall);
        Assert.Equal(DateTimeOffset.Parse("2026-08-30T00:00:00Z"), evidence.StartedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-30T00:01:00Z"), evidence.CompletedUtc);
        Assert.Equal(12, evidence.Checks.Count);
        Assert.All(evidence.Checks, check => Assert.Equal("Passed", check.Result));
        Assert.Equal(0, evidence.Teardown.ResourceCount);
        Assert.True(evidence.Teardown.TemporaryDirectoryRemoved);
        Assert.Matches("^[0-9a-f]{64}$", evidence.Sha256);
        Assert.Equal(evidence.ToCanonicalUtf8(), fromUtf8.ToCanonicalUtf8());
        Assert.Equal(evidence.Sha256, fromUtf8.Sha256);
        Assert.Equal(Cp6P09Json.Sha256Hex(evidence.ToCanonicalUtf8()), evidence.Sha256);
    }

    [Fact]
    public void Parse_SecretNegativeExample_RejectsWithStableCheckId()
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RehearsalEvidence.Parse(P09ContractTestData.ReadExample("rehearsal-evidence.secret.invalid.json")));

        Assert.Equal("unsafe-evidence", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(InvalidShapeMutations))]
    public void Parse_InvalidShapeTimeHashOrTraceMutation_ThrowsStableCheckId(string mutation, string expectedCheckId)
    {
        var json = MutateCanonical(mutation);

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RehearsalEvidence.Parse(json));

        Assert.Equal(expectedCheckId, exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(FalsePassMutations))]
    public void Parse_PassedEvidenceWithFailureOrResidue_ThrowsFalsePass(string mutation)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RehearsalEvidence.Parse(MutateCanonical(mutation)));

        Assert.Equal("false-pass", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(UnsafeEvidenceMutations))]
    public void Parse_UnsafeNamesCredentialsOrMachinePaths_ThrowsStableCheckId(string mutation, string expectedCheckId)
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RehearsalEvidence.Parse(MutateCanonical(mutation)));

        Assert.Equal(expectedCheckId, exception.CheckId);
    }

    [Fact]
    public void Parse_UriUserInfo_DoesNotEchoCredentialBearingEvidenceInExceptionMessage()
    {
        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RehearsalEvidence.Parse(MutateCanonical("uri-userinfo-password")));

        Assert.Equal("unsafe-evidence", exception.CheckId);
        Assert.DoesNotContain("publisher", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("obvious-fake-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(UnsafeSummaryCorpus))]
    public void Parse_AdversarialSummaryCorpus_RejectsUnsafeEvidence(string summary)
    {
        var root = P09ContractTestData.ParseValidEvidence();
        root["checks"]![0]!["summary"] = summary;

        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RehearsalEvidence.Parse(Cp6P09Json.Canonicalize(root.ToJsonString())));

        Assert.Equal("unsafe-evidence", exception.CheckId);
    }

    [Theory]
    [MemberData(nameof(SafeSummaryCorpus))]
    public void Parse_SimpleAsciiSummaryCorpus_Accepts(string summary)
    {
        var root = P09ContractTestData.ParseValidEvidence();
        root["checks"]![0]!["summary"] = summary;

        Assert.Equal(
            "Passed",
            Cp6P09RehearsalEvidence.Parse(Cp6P09Json.Canonicalize(root.ToJsonString())).Overall);
    }

    [Fact]
    public void Parse_DuplicateJsonProperty_RejectsBeforeMaterialization()
    {
        var valid = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");
        var duplicate = valid.Replace(
            "\"schemaVersion\":\"1\"",
            "\"schemaVersion\":\"1\",\"schemaVersion\":\"1\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RehearsalEvidence.Parse(duplicate));

        Assert.Equal("duplicate-property", exception.CheckId);
    }

    [Fact]
    public void Parse_NonCanonicalEvidence_RejectsWhitespaceOrPropertyOrder()
    {
        var valid = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");
        var whitespace = valid.Insert(1, " ");
        var reordered = JsonNode.Parse(valid)!.AsObject();
        var schemaVersion = reordered["schemaVersion"]!.DeepClone();
        reordered.Remove("schemaVersion");
        reordered.Add("schemaVersion", schemaVersion);

        Assert.Equal(
            "non-canonical-evidence",
            Assert.Throws<Cp6P09ContractException>(() => Cp6P09RehearsalEvidence.Parse(whitespace)).CheckId);
        Assert.Equal(
            "non-canonical-evidence",
            Assert.Throws<Cp6P09ContractException>(() =>
                Cp6P09RehearsalEvidence.Parse(reordered.ToJsonString())).CheckId);
    }

    [Theory]
    [MemberData(nameof(TeardownNumericProperties))]
    public void Parse_NegativeZeroTeardownNumber_HasZeroCanonicalIdentityAndIsRejectedAsNoncanonical(
        string propertyName)
    {
        var valid = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");
        var negativeZero = valid.Replace(
            $"\"{propertyName}\":0",
            $"\"{propertyName}\":-0.0",
            StringComparison.Ordinal);

        Assert.NotEqual(valid, negativeZero);
        Assert.Equal(valid, Cp6P09Json.Canonicalize(negativeZero));
        Assert.Equal(
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(valid)),
            Cp6P09Json.Sha256Hex(Encoding.UTF8.GetBytes(Cp6P09Json.Canonicalize(negativeZero))));
        Assert.Equal(
            "non-canonical-evidence",
            Assert.Throws<Cp6P09ContractException>(() => Cp6P09RehearsalEvidence.Parse(negativeZero)).CheckId);
    }

    [Fact]
    public void Parse_InvalidUtf8_ThrowsInvalidJson()
    {
        byte[] invalidUtf8 = [(byte)'{', (byte)'\"', (byte)'x', (byte)'\"', (byte)':', (byte)'\"', 0xFF, (byte)'\"', (byte)'}'];

        var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RehearsalEvidence.Parse(invalidUtf8));

        Assert.Equal("invalid-json", exception.CheckId);
    }

    [Fact]
    public void Parse_RejectsWrongProfileHashBeforeValidateAgainstAndAcceptsBoundOverload()
    {
        var profile = Cp6P09RuntimeProfile.Parse(
            P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json"));
        var valid = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");
        var wrongHash = P09ContractTestData.ParseValidEvidence();
        wrongHash["profileSha256"] = new string('f', 64);

        Cp6P09RehearsalEvidence.Parse(valid, profile);
        var exception = Assert.Throws<Cp6P09ContractException>(() =>
            Cp6P09RehearsalEvidence.Parse(Cp6P09Json.Canonicalize(wrongHash.ToJsonString())));

        Assert.Equal("profile-mismatch", exception.CheckId);
    }

    [Fact]
    public void FailedEvidence_MayRecordFailedChecksAndResidueWithoutClaimingPass()
    {
        var root = P09ContractTestData.ParseValidEvidence();
        root["overall"] = "Failed";
        root["checks"]![0]!["result"] = "Failed";
        root["teardown"]!["commandExitCode"] = 1;
        root["teardown"]!["containerCount"] = 2;
        root["teardown"]!["temporaryDirectoryRemoved"] = false;

        var evidence = Cp6P09RehearsalEvidence.Parse(Cp6P09Json.Canonicalize(root.ToJsonString()));

        Assert.Equal("Failed", evidence.Overall);
        Assert.Equal(2, evidence.Teardown.ResourceCount);
    }

    [Fact]
    public void ToCanonicalUtf8AndChecks_ReturnDefensiveImmutableViews()
    {
        var evidence = Cp6P09RehearsalEvidence.Parse(
            P09ContractTestData.ReadExample("rehearsal-evidence.valid.json"));
        var first = evidence.ToCanonicalUtf8();
        var expected = first.ToArray();

        first[0] = (byte)'[';
        var second = evidence.ToCanonicalUtf8();
        var checks = Assert.IsAssignableFrom<IList<Cp6P09EvidenceCheck>>(evidence.Checks);

        Assert.NotSame(first, second);
        Assert.Equal(expected, second);
        Assert.Throws<NotSupportedException>(() => checks.Add(evidence.Checks[0]));
        Assert.False(typeof(Cp6P09EvidenceCheck).GetProperty(nameof(Cp6P09EvidenceCheck.Id))!.CanWrite);
        Assert.False(typeof(Cp6P09EvidenceTeardown).GetProperty(nameof(Cp6P09EvidenceTeardown.ContainerCount))!.CanWrite);
    }

    private static string MutateCanonical(string mutation)
    {
        var root = P09ContractTestData.ParseValidEvidence();
        var checks = root["checks"]!.AsArray();
        var teardown = root["teardown"]!.AsObject();
        var trace = root["trace"]!.AsObject();

        switch (mutation)
        {
            case "missing-root": root.Remove("runtime"); break;
            case "unknown-root": root["unexpected"] = true; break;
            case "wrong-root-type": root["schemaVersion"] = 1; break;
            case "wrong-nested-type": teardown["containerCount"] = "0"; break;
            case "uppercase-hash": root["profileSha256"] = new string('A', 64); break;
            case "short-hash": root["profileSha256"] = new string('a', 63); break;
            case "uppercase-git-sha": root["platformGitSha"] = new string('C', 40); break;
            case "wrong-profile-id": root["profileId"] = "cp6-platform-p09-other"; break;
            case "duplicate-check": checks.Add(checks[0]!.DeepClone()); break;
            case "missing-check": checks.RemoveAt(0); break;
            case "reordered-check": SwapFirstTwo(checks); break;
            case "non-utc-start": root["startedUtc"] = "2026-08-30T00:00:00-04:00"; break;
            case "invalid-time": root["completedUtc"] = "not-a-time"; break;
            case "completed-before-start": root["completedUtc"] = "2026-08-29T23:59:59Z"; break;
            case "trace-publisher-equals-receiver": trace["receiverSpanId"] = trace["publisherSpanId"]!.DeepClone(); break;
            case "trace-invoke-parent-equals-child": trace["invokedSpanId"] = trace["invokerSpanId"]!.DeepClone(); break;
            case "zero-trace-id": trace["traceId"] = new string('0', 32); break;
            case "zero-invocation-trace-id": trace["invocationTraceId"] = new string('0', 32); break;
            case "zero-publisher-span-id": trace["publisherSpanId"] = new string('0', 16); break;
            case "zero-receiver-span-id": trace["receiverSpanId"] = new string('0', 16); break;
            case "zero-invoker-span-id": trace["invokerSpanId"] = new string('0', 16); break;
            case "zero-invoked-span-id": trace["invokedSpanId"] = new string('0', 16); break;
            case "negative-teardown-count": teardown["containerCount"] = -1; break;
            case "empty-summary": checks[0]!["summary"] = string.Empty; break;
            case "long-summary": checks[0]!["summary"] = new string('a', 161); break;
            case "multiline-summary": checks[0]!["summary"] = "line one\nline two"; break;
            case "invalid-check-id": checks[0]!["id"] = "invalid check id"; break;
            case "too-precise-timestamp": root["startedUtc"] = "2026-08-30T00:00:00.12345678Z"; break;
            case "oversized-teardown-count": teardown["containerCount"] = 2_147_483_648L; break;
            case "failed-check": checks[0]!["result"] = "Failed"; break;
            case "teardown-exit": teardown["commandExitCode"] = 1; break;
            case "teardown-container": teardown["containerCount"] = 1; break;
            case "teardown-network": teardown["networkCount"] = 1; break;
            case "teardown-volume": teardown["volumeCount"] = 1; break;
            case "teardown-image": teardown["imageCount"] = 1; break;
            case "teardown-directory": teardown["temporaryDirectoryRemoved"] = false; break;
            case "windows-path": checks[0]!["summary"] = "artifact at C:\\agent\\work"; break;
            case "embedded-windows-backslash-path": checks[0]!["summary"] = "artifact=C:\\agent\\work"; break;
            case "embedded-windows-forward-slash-path": checks[0]!["summary"] = "artifact=C:/agent/work"; break;
            case "unc-path": checks[0]!["summary"] = "artifact at \\\\server\\share"; break;
            case "embedded-unc-path": checks[0]!["summary"] = "artifact=\\\\server\\share"; break;
            case "unix-home-path": checks[0]!["summary"] = "artifact under /home/runner/work"; break;
            case "unix-users-path": checks[0]!["summary"] = "artifact under /Users/runner/work"; break;
            case "unix-temp-path": checks[0]!["summary"] = "artifact under /tmp/p09"; break;
            case "unix-var-path": checks[0]!["summary"] = "artifact=/var/lib/docker"; break;
            case "unix-etc-path": checks[0]!["summary"] = "artifact=/etc/kafka/config"; break;
            case "unix-opt-path": checks[0]!["summary"] = "artifact=/opt/cp6/runtime"; break;
            case "unix-srv-path": checks[0]!["summary"] = "artifact=/srv/cp6/runtime"; break;
            case "unix-workspace-path": checks[0]!["summary"] = "artifact=/workspace/cp6/runtime"; break;
            case "colon-unix-path": checks[0]!["summary"] = "artifact:/var/lib/docker"; break;
            case "file-uri-path": checks[0]!["summary"] = "artifact file:///var/lib/docker"; break;
            case "forward-unc-path": checks[0]!["summary"] = "artifact=//server/share"; break;
            case "unicode-unix-path": checks[0]!["summary"] = "artifact=/用户/路径"; break;
            case "lookalike-http-path": checks[0]!["summary"] = "artifact=xhttp://server/share"; break;
            case "malformed-http-path": checks[0]!["summary"] = "artifact=http:///var/lib/docker"; break;
            case "uri-userinfo-password": checks[0]!["summary"] = "endpoint https://publisher:obvious-fake-secret@example.test/p09 accepted"; break;
            case "uri-userinfo-username": checks[0]!["summary"] = "endpoint https://publisher@example.test/p09 accepted"; break;
            case "uri-userinfo-percent-encoded": checks[0]!["summary"] = "endpoint https://publisher%3Aobvious-fake-secret@example.test/p09 accepted"; break;
            case "password-assignment": checks[0]!["summary"] = "password=obvious-fake-value"; break;
            case "password-tab-assignment": checks[0]!["summary"] = "password\t=\tobvious-fake-value"; break;
            case "password-nbsp-assignment": checks[0]!["summary"] = "password\u00a0=\u00a0obvious-fake-value"; break;
            case "password-next-line-assignment": checks[0]!["summary"] = "password\u0085=\u0085obvious-fake-value"; break;
            case "token-assignment": checks[0]!["summary"] = "token=obvious-fake-value"; break;
            case "connection-string-assignment": checks[0]!["summary"] = "connectionString=obvious-fake-value"; break;
            case "bearer-credential": checks[0]!["summary"] = "Bearer obvious-fake-credential"; break;
            case "bearer-tab-credential": checks[0]!["summary"] = "Bearer\tobvious-fake-credential"; break;
            case "bearer-nbsp-credential": checks[0]!["summary"] = "Bearer\u00a0obvious-fake-credential"; break;
            case "bearer-next-line-credential": checks[0]!["summary"] = "Bearer\u0085obvious-fake-credential"; break;
            case "secret-field-password": trace["PASSWORD"] = "obvious-fake-value"; break;
            case "secret-field-token": trace["Token"] = "obvious-fake-value"; break;
            case "secret-field-connection-string": trace["connectionString"] = "obvious-fake-value"; break;
            case "secret-field-secret-value": trace["secretValue"] = "obvious-fake-value"; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        return Cp6P09Json.Canonicalize(root.ToJsonString());
    }

    private static void SwapFirstTwo(JsonArray values)
    {
        var first = values[0]!.DeepClone();
        var second = values[1]!.DeepClone();
        values[0] = second;
        values[1] = first;
    }
}
