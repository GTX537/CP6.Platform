using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CP6.Platform.ArchitectureTests;

public sealed class RepositoryArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly Regex ForbiddenBackendNames = new(
        @"Grafana|Prometheus",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ForbiddenTempoNames = new(
        @"(?<![A-Za-z0-9])(?i:tempo)(?![A-Za-z0-9])|(?:Tempo|tempo|TEMPO)(?![a-z])|(?i:tempo(?:exporteroptions?|exporters?|backends?|clients?|services?|collectors?|sinks?|settings?|endpoints?|configs?))",
        RegexOptions.CultureInvariant);

    public static TheoryData<string> ForbiddenTempoCases => new()
    {
        "tempo",
        "Tempo",
        "TEMPO",
        "TempoExporter",
        "TempoBackend",
        "AddTempoExporter",
        "AddTempoClient",
        "TempoClient",
        "TempoService",
        "TempoCollector",
        "TempoSink",
        "TempoSettings",
        "TempoEndpoint",
        "TempoConfig",
        "TempoExporterOptions",
        "OpenTelemetryTempoExporter",
        "tempoExporter",
        "tempo_endpoint",
        "TEMPO_ENDPOINT",
        "addTempoExporter",
        "temporaryTempoDirectory",
        "tempo:4317",
        "http://tempo:4317",
        "tempo backend",
        "Tempo backend",
        "tempo-client",
        "Tempo.Endpoint",
        "Tempo/Exporter",
        "TEMPO-ENDPOINT",
        "AddTempo",
        "AddTempo.Endpoint",
        "AddTempo/Exporter",
        "AddTempo-Exporter",
        "AddTempo_Exporter",
        "AddTempo backend",
        "UseTempo:4317",
        "OpenTelemetryTempo:4317",
        "tempoexporter",
        "tempobackend",
        "tempoclient",
        "temposervice",
        "tempocollector",
        "temposink",
        "temposettings",
        "tempoendpoint",
        "tempoconfig",
        "tempoexporteroptions",
        "Tempoexporter",
        "Tempobackend",
        "Tempoclient",
        "Temposervice",
        "Tempocollector",
        "Temposink",
        "Temposettings",
        "Tempoendpoint",
        "Tempoconfig",
        "Tempoexporteroptions",
        "TeMpOExporter",
        "TeMpOBackend",
        "TeMpOClient",
        "TeMpOService",
        "TeMpOCollector",
        "TeMpOSink",
        "TeMpOSettings",
        "TeMpOEndpoint",
        "TeMpOConfig",
        "TeMpOExporterOptions"
    };

    public static TheoryData<string> AllowedTemporaryCases => new()
    {
        "tempOutput",
        "TempOutput",
        "tempOffset",
        "TemporalWindow",
        "temporalWindow",
        "TemporallyConsistent",
        "temporallyConsistent",
        "TemporarilyRemoved",
        "TemporaryFileRemoved",
        "temporaryFileRemoved",
        "contemporaryOperation",
        "contemporaneousOperation",
        "temporaryDirectoryRemoved",
        "TemporaryDirectoryRemoved"
    };

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CP6.Platform.Contracts"] = [],
            ["CP6.Platform.Deployment"] = [],
            ["CP6.Platform.Release"] = [],
            ["CP6.Platform.Abstractions"] = ["CP6.Platform.Contracts"],
            ["CP6.Platform.AspNetCore"] = ["CP6.Platform.Abstractions", "CP6.Platform.Contracts"],
            ["CP6.Platform.Messaging"] = ["CP6.Platform.Abstractions", "CP6.Platform.Contracts"],
            ["CP6.Platform.EntityFramework"] = ["CP6.Platform.Abstractions", "CP6.Platform.Contracts"],
            ["CP6.Platform.Testing"] =
            [
                "CP6.Platform.Abstractions",
                "CP6.Platform.AspNetCore",
                "CP6.Platform.Contracts",
                "CP6.Platform.EntityFramework",
                "CP6.Platform.Messaging"
            ]
        };

    [Fact]
    public void SourceProjects_ExactlyMatchApprovedRuntimeAndTestingSet()
    {
        var actual = LoadProjects().Keys.Order(StringComparer.Ordinal).ToArray();
        var expected = ExpectedDependencies.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProjectReferences_ExactlyMatchApprovedDependencyDirection()
    {
        foreach (var (packageId, project) in LoadProjects())
        {
            var actual = project.Document.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var expected = ExpectedDependencies[packageId].Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Contracts_HasNoExternalOrInternalDependencies()
    {
        var contracts = LoadProjects()["CP6.Platform.Contracts"].Document;

        Assert.Empty(contracts.Descendants("ProjectReference"));
        Assert.Empty(contracts.Descendants("PackageReference"));
        Assert.Empty(contracts.Descendants("FrameworkReference"));
    }

    [Fact]
    public void Deployment_HasNoExternalInternalOrFrameworkDependencies()
    {
        var deployment = LoadProjects()["CP6.Platform.Deployment"].Document;

        Assert.Empty(deployment.Descendants("ProjectReference"));
        Assert.Empty(deployment.Descendants("PackageReference"));
        Assert.Empty(deployment.Descendants("FrameworkReference"));
    }

    [Fact]
    public void Release_package_is_independent_and_owns_only_release_contract_assets()
    {
        var projects = LoadProjects();
        var release = projects["CP6.Platform.Release"].Document;

        Assert.Empty(release.Descendants("ProjectReference"));
        Assert.Empty(release.Descendants("PackageReference"));
        Assert.Empty(release.Descendants("FrameworkReference"));

        var packed = GetProjectItems(release)
            .Where(item => string.Equals(GetItemValue(item, "Pack"), "true", StringComparison.OrdinalIgnoreCase))
            .Select(item => (item.Attribute("Include")?.Value, GetItemValue(item, "PackagePath")))
            .ToArray();
        Assert.Equal(
            [("../../contracts/release/v1/**/*", "contracts/release/v1/%(RecursiveDir)%(Filename)%(Extension)")],
            packed);

        foreach (var (packageId, project) in projects.Where(project => project.Key != "CP6.Platform.Release"))
        {
            Assert.DoesNotContain(
                GetProjectItems(project.Document),
                item => (item.Attribute("Include")?.Value ?? string.Empty)
                    .Contains("contracts/release", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                project.Document.Descendants("ProjectReference"),
                reference => string.Equals(
                    Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value),
                    "CP6.Platform.Release",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OtherSourceProjects_DoNotReferenceDeployment()
    {
        foreach (var (_, project) in LoadProjects()
                     .Where(project => project.Key != "CP6.Platform.Deployment"))
        {
            Assert.DoesNotContain(
                project.Document.Descendants("ProjectReference"),
                reference => string.Equals(
                    Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value),
                    "CP6.Platform.Deployment",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Deployment_IsOnlyProjectPackingP09ContractsAndDeploymentAssets()
    {
        var projects = LoadProjects();
        var deploymentAssets = GetProjectItems(projects["CP6.Platform.Deployment"].Document)
            .Where(item => !string.IsNullOrWhiteSpace(GetItemValue(item, "Pack")))
            .Select(item => (
                ItemType: item.Name.LocalName,
                Include: item.Attribute("Include")?.Value ?? string.Empty,
                Update: item.Attribute("Update")?.Value ?? string.Empty,
                Remove: item.Attribute("Remove")?.Value ?? string.Empty,
                Pack: GetItemValue(item, "Pack"),
                PackagePath: GetItemValue(item, "PackagePath")))
            .ToArray();
        var expectedDeploymentAssets = new[]
        {
            (
                ItemType: "None",
                Include: "../../contracts/p09/**/*",
                Update: string.Empty,
                Remove: string.Empty,
                Pack: "true",
                PackagePath: "contracts/p09/%(RecursiveDir)%(Filename)%(Extension)"),
            (
                ItemType: "None",
                Include: "../../deploy/p09/**/*",
                Update: string.Empty,
                Remove: string.Empty,
                Pack: "true",
                PackagePath: "deploy/p09/%(RecursiveDir)%(Filename)%(Extension)")
        };

        Assert.Equal(expectedDeploymentAssets, deploymentAssets);

        foreach (var (_, project) in projects.Where(project => project.Key != "CP6.Platform.Deployment"))
        {
            var ownershipValues = GetProjectItems(project.Document)
                .SelectMany(item => new[]
                {
                    item.Attribute("Include")?.Value,
                    item.Attribute("Update")?.Value,
                    item.Attribute("Remove")?.Value,
                    GetItemValue(item, "PackagePath")
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
            var projectText = File.ReadAllText(project.Path);

            Assert.DoesNotContain(
                ownershipValues,
                value => value.Contains("p09", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("p09", projectText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SourceProjects_UseOnlyApprovedExternalDependenciesAndFrameworks()
    {
        foreach (var (packageId, project) in LoadProjects())
        {
            var packageReferences = project.Document.Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")!.Value)
                .ToArray();
            var frameworkReferences = project.Document.Descendants("FrameworkReference")
                .Select(reference => reference.Attribute("Include")!.Value)
                .ToArray();

            if (packageId == "CP6.Platform.AspNetCore")
            {
                Assert.Equal(
                    [
                        "Microsoft.AspNetCore.Authentication.JwtBearer",
                        "Microsoft.Extensions.Http.Resilience",
                        "OpenTelemetry.Extensions.Hosting",
                        "OpenTelemetry.Instrumentation.AspNetCore",
                        "OpenTelemetry.Instrumentation.Http",
                        "Yarp.ReverseProxy"
                    ],
                    packageReferences.Order(StringComparer.Ordinal));
                Assert.Equal(["Microsoft.AspNetCore.App"], frameworkReferences);
            }
            else if (packageId == "CP6.Platform.Messaging")
            {
                Assert.Equal(
                    ["CloudNative.CloudEvents", "CloudNative.CloudEvents.SystemTextJson", "Dapr.Client", "JsonSchema.Net"],
                    packageReferences.Order(StringComparer.Ordinal));
                Assert.Empty(frameworkReferences);
            }
            else if (packageId == "CP6.Platform.EntityFramework")
            {
                Assert.Equal(
                    ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational"],
                    packageReferences.Order(StringComparer.Ordinal));
                Assert.Empty(frameworkReferences);
            }
            else if (packageId == "CP6.Platform.Testing")
            {
                Assert.Empty(packageReferences);
                Assert.Equal(["Microsoft.AspNetCore.App"], frameworkReferences);
            }
            else
            {
                Assert.Empty(packageReferences);
                Assert.Empty(frameworkReferences);
            }
        }
    }

    [Fact]
    public void P08_DependencyAndPackageBoundary_IsExact()
    {
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var centralPackages = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
        var versions = centralPackages.Descendants("PackageVersion")
            .ToDictionary(
                package => package.Attribute("Include")!.Value,
                package => package.Attribute("Version")!.Value,
                StringComparer.Ordinal);
        var projects = LoadProjects();

        Assert.Equal("0.8.0", buildProperties.Descendants("VersionPrefix").Single().Value);
        Assert.Equal("alpha.2", buildProperties.Descendants("VersionSuffix").Single().Value);
        Assert.Equal("1.18.0", versions["OpenTelemetry.Extensions.Hosting"]);
        Assert.Equal("1.18.0", versions["OpenTelemetry.Instrumentation.AspNetCore"]);
        Assert.Equal("1.18.0", versions["OpenTelemetry.Instrumentation.Http"]);
        Assert.Equal("10.9.0", versions["Microsoft.Extensions.Http.Resilience"]);

        var aspNetCorePackages = projects["CP6.Platform.AspNetCore"].Document.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "Microsoft.AspNetCore.Authentication.JwtBearer",
                "Microsoft.Extensions.Http.Resilience",
                "OpenTelemetry.Extensions.Hosting",
                "OpenTelemetry.Instrumentation.AspNetCore",
                "OpenTelemetry.Instrumentation.Http",
                "Yarp.ReverseProxy"
            ],
            aspNetCorePackages);

        var messagingReferences = projects["CP6.Platform.Messaging"].Document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["CP6.Platform.Abstractions", "CP6.Platform.Contracts"], messagingReferences);

        Assert.Equal("false", projects["CP6.Platform.Testing"].Document.Descendants("IsPackable").Single().Value);
        Assert.All(
            projects.Where(project => project.Key != "CP6.Platform.Testing"),
            project => Assert.Equal("true", project.Value.Document.Descendants("IsPackable").Single().Value));

        foreach (var project in projects.Where(project => project.Key != "CP6.Platform.Testing"))
        {
            Assert.DoesNotContain(
                project.Value.Document.Descendants("ProjectReference"),
                reference => string.Equals(
                    Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value),
                    "CP6.Platform.Testing",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void P08_PackageEvidenceAndProductionSafetyGuards_AreEncoded()
    {
        var productionProjects = LoadProjects()
            .Where(project => project.Key != "CP6.Platform.Testing")
            .ToArray();
        foreach (var (packageId, project) in productionProjects)
        {
            var packedAssets = project.Document.Descendants("None")
                .Where(item => string.Equals(item.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Attribute("PackagePath")?.Value ?? string.Empty)
                .ToArray();
            if (packageId == "CP6.Platform.Contracts")
            {
                Assert.Equal(["contracts/observability/%(RecursiveDir)%(Filename)%(Extension)"], packedAssets);
            }
            else if (packageId == "CP6.Platform.Messaging")
            {
                Assert.Equal(
                    [
                        "contracts/contract-bundle.v1.json",
                        "contracts/events/%(RecursiveDir)%(Filename)%(Extension)"
                    ],
                    packedAssets);
            }
            else if (packageId == "CP6.Platform.Deployment")
            {
                Assert.Equal(
                    [
                        "contracts/p09/%(RecursiveDir)%(Filename)%(Extension)",
                        "deploy/p09/%(RecursiveDir)%(Filename)%(Extension)"
                    ],
                    packedAssets);
            }
            else if (packageId == "CP6.Platform.Release")
            {
                Assert.Equal(["contracts/release/v1/%(RecursiveDir)%(Filename)%(Extension)"], packedAssets);
            }
            else
            {
                Assert.Empty(packedAssets);
            }

            var sourceRoot = Path.GetDirectoryName(project.Path)!;
            var productionText = string.Join(
                '\n',
                Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"))
                    .Select(File.ReadAllText));
            Assert.DoesNotContain("CP6.Platform.Testing", productionText, StringComparison.Ordinal);
            foreach (var forbidden in new[]
            {
                "AddOtlpExporter",
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                "http://localhost:4317",
                "http://localhost:4318",
                "collector:4317",
                "BEGIN PRIVATE KEY",
                "password=",
                "MapGet(\"/deploy",
                "MapPost(\"/deploy"
            })
            {
                Assert.DoesNotContain(forbidden, productionText, StringComparison.OrdinalIgnoreCase);
            }

            Assert.DoesNotMatch(ForbiddenBackendNames, productionText);
            Assert.DoesNotMatch(ForbiddenTempoNames, productionText);
        }

        var verify = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify.ps1"));
        var pack = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "pack-release.ps1"));
        Assert.Contains("SloEvidencePackageContent", verify, StringComparison.Ordinal);
        Assert.Contains("PackageContentSafety", verify, StringComparison.Ordinal);
        Assert.Contains("CP6.Platform.Testing", verify, StringComparison.Ordinal);
        Assert.Contains("lib/net8.0/", verify, StringComparison.Ordinal);
        Assert.Contains("*.snupkg", pack, StringComparison.Ordinal);
        Assert.Contains("contracts/observability", pack, StringComparison.Ordinal);
        Assert.Contains("CP6.Platform.Testing", pack, StringComparison.Ordinal);
        Assert.Contains("$packageVersion = '0.8.0-alpha.2'", verify, StringComparison.Ordinal);
        Assert.Contains("[string]$PackageVersion = '0.8.0-alpha.2'", pack, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTempoCases))]
    public void ProductionBackendGuard_RejectsTempoTokensAndCamelIdentifiers(string value)
    {
        Assert.Matches(ForbiddenTempoNames, value);
    }

    [Theory]
    [MemberData(nameof(AllowedTemporaryCases))]
    public void ProductionBackendGuard_AllowsTemporaryAndTemporalVocabulary(string value)
    {
        Assert.DoesNotMatch(ForbiddenTempoNames, value);
    }

    [Fact]
    public void ProductionBackendGuard_RejectsGrafanaAndPrometheusCaseInsensitively()
    {
        Assert.Matches(ForbiddenBackendNames, "Grafana.Exporter");
        Assert.Matches(ForbiddenBackendNames, "Prometheus backend");
        Assert.Matches(ForbiddenBackendNames, "AddPrometheusExporter");
        Assert.Matches(ForbiddenBackendNames, "OpenTelemetryPrometheusExporter");
        Assert.Matches(ForbiddenBackendNames, "GrafanaClient");
        Assert.Matches(ForbiddenBackendNames, "GRAFANA_ENDPOINT");
    }

    [Fact]
    public void P09EvidenceImplementation_SeparatesFacadeValidationAndSafetyPolicy()
    {
        var deploymentRoot = Path.Combine(RepositoryRoot, "src", "CP6.Platform.Deployment");
        var facade = Path.Combine(deploymentRoot, "Cp6P09RehearsalEvidence.cs");
        var validator = Path.Combine(deploymentRoot, "Cp6P09RehearsalEvidence.Validator.cs");
        var safety = Path.Combine(deploymentRoot, "Cp6P09RehearsalEvidence.Safety.cs");

        Assert.True(File.Exists(validator), "Missing focused Evidence shape/fixed-invariant validator file.");
        Assert.True(File.Exists(safety), "Missing focused Evidence safety policy file.");
        Assert.InRange(File.ReadAllLines(facade).Length, 1, 300);
    }

    [Fact]
    public void P08_PublicationWorkflow_AlwaysPreservesCompleteEvidence()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "publish-alpha.yml"));
        const string evidenceStepMarker = "      - name: Preserve publication evidence";
        var evidenceStepStart = workflow.IndexOf(evidenceStepMarker, StringComparison.Ordinal);

        Assert.True(evidenceStepStart >= 0, "Missing publication evidence upload step.");
        var evidenceStep = workflow[evidenceStepStart..];
        var uploadPaths = evidenceStep.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("artifacts/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            [
                "artifacts/release/**",
                "artifacts/verify/**",
                "artifacts/p05-integration/**",
                "artifacts/p06-sql-integration/**"
            ],
            uploadPaths);
        foreach (var required in new[]
        {
            "if: always()",
            "uses: actions/upload-artifact@",
            "name: p08-alpha-${{ inputs.expected_commit }}",
            "if-no-files-found: error",
            "retention-days: 30"
        })
        {
            Assert.Contains(required, evidenceStep, StringComparison.Ordinal);
        }
        Assert.Contains(
            "./eng/pack-release.ps1 -OutputDirectory artifacts/release -PackageVersion 0.8.0-alpha.2",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void P08_Documentation_IsCompleteAndSafe()
    {
        var documents = new[]
        {
            "docs/P08-OBSERVABILITY-RESILIENCE.md",
            "docs/P08-PUBLICATION.md",
            "docs/runbooks/P08-TRACE-EXPORTER.md",
            "docs/runbooks/P08-HEALTH-READINESS.md",
            "docs/runbooks/P08-HTTP-RESILIENCE.md",
            "docs/runbooks/P08-MESSAGING-BACKLOG.md",
            "docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md"
        };
        var content = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in documents)
        {
            var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing required P08 document: {relativePath}");
            var text = File.ReadAllText(path);
            content.Add(relativePath, text);
            Assert.Contains(
                "P08 final decision: `Frozen / Consumable`. Effective condition: the S06 change containing this declaration is merged to `main` and its exact-main `platform-validation` passes `ubuntu-latest`, `windows-latest`, `ubuntu-dapr-kafka`, and `ubuntu-sql-server`; until then the PR head is only a final-audit candidate.",
                text,
                StringComparison.Ordinal);
            foreach (var forbidden in new[] { "TODO", "TBD", "FIXME" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        foreach (var required in new[]
        {
            "P08 release/SLO evidence frozen in `0.8.0-alpha.2`",
            "P08 telemetry/release abstractions frozen in `0.8.0-alpha.2`",
            "P08 observability/health/resilience contract frozen in `0.8.0-alpha.2`",
            "P08 trace/metric contract frozen in `0.8.0-alpha.2`",
            "P08 observer-only telemetry contract frozen in `0.8.0-alpha.2`"
        })
        {
            Assert.Contains(required, readme, StringComparison.Ordinal);
        }
        foreach (var forbidden in new[]
        {
            "P08 release/SLO evidence candidate",
            "P08 observability/health/resilience candidate",
            "P08 trace/metric candidate",
            "P08 observer-only telemetry candidate"
        })
        {
            Assert.DoesNotContain(forbidden, readme, StringComparison.Ordinal);
        }

        var combined = string.Join('\n', content.Values);
        foreach (var required in new[]
        {
            "/health/live",
            "/health/startup",
            "/health/ready",
            "/health/release",
            "cp6.http.outbound",
            "cp6.messaging.dapr.invoke",
            "cp6.messaging.publish",
            "cp6.messaging.consume",
            "cp6.outbox.dispatch",
            "cp6.inbox.process",
            "OperationNotAllowed",
            "IdempotencyRequired",
            "AttemptTimeout",
            "TotalTimeout",
            "CircuitOpen",
            "https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json",
            "OpenTelemetry Collector",
            "host-owned exporter",
            "CRM Worker",
            "productionSloClaimed=false"
        })
        {
            Assert.Contains(required, combined, StringComparison.Ordinal);
        }

        var runbooks = documents.Skip(2).ToArray();
        var runbookIds = new[]
        {
            "CP6-P08-TRACE-001",
            "CP6-P08-HEALTH-001",
            "CP6-P08-RESILIENCE-001",
            "CP6-P08-MESSAGING-001",
            "CP6-P08-RELEASE-001"
        };
        for (var index = 0; index < runbooks.Length; index++)
        {
            var runbook = content[runbooks[index]];
            Assert.Contains(runbookIds[index], runbook, StringComparison.Ordinal);
            foreach (var heading in new[]
            {
                "## Symptoms",
                "## Impact",
                "## Stable query ID",
                "## Safe diagnosis",
                "## Containment",
                "## Recovery",
                "## Validation",
                "## Escalation",
                "## Evidence retention"
            })
            {
                Assert.Contains(heading, runbook, StringComparison.Ordinal);
            }
        }

        var safetyText = combined;
        foreach (var allowedUrl in new[]
        {
            "https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json",
            "https://github.com/GTX537/CP6.Platform/actions/runs/33303723733",
            "https://github.com/GTX537/CP6.Platform/pull/21",
            "https://github.com/GTX537/CP6.Platform/actions/runs/33305166884",
            "https://github.com/GTX537/CP6.Platform/actions/runs/33305345694",
            "https://github.com/GTX537/CP6.Platform/pull/23",
            "https://github.com/GTX537/CP6.Platform/actions/runs/33320438234",
            "https://github.com/GTX537/CP6.Platform/actions/runs/33320608737",
            "https://github.com/GTX537/CP6.Platform/actions/runs/33320840180",
            "https://github.com/GTX537/CP6.CRM/pull/33",
            "https://github.com/GTX537/CP6.CRM/actions/runs/33329003327",
            "https://github.com/GTX537/CP6.CRM/actions/runs/33329320097",
            "https://github.com/GTX537/CP6.CRM/pull/34",
            "https://github.com/GTX537/CP6.CRM/actions/runs/33330377723",
            "https://github.com/GTX537/CP6.CRM/actions/runs/33330705446",
            "https://github.com/GTX537/CP6.CRM/pull/35",
            "https://github.com/GTX537/CP6.CRM/actions/runs/33332328534",
            "https://github.com/GTX537/CP6.CRM/actions/runs/33332741550",
            "https://github.com/users/GTX537/packages/nuget/CP6.Platform.Abstractions/1188299233",
            "https://github.com/users/GTX537/packages/nuget/CP6.Platform.AspNetCore/1188299259",
            "https://github.com/users/GTX537/packages/nuget/CP6.Platform.Contracts/1188299302",
            "https://github.com/users/GTX537/packages/nuget/CP6.Platform.EntityFramework/1188299341",
            "https://github.com/users/GTX537/packages/nuget/CP6.Platform.Messaging/1188299373"
        })
        {
            safetyText = safetyText.Replace(allowedUrl, string.Empty, StringComparison.Ordinal);
        }
        foreach (var forbidden in new[]
        {
            "http://",
            "https://",
            "password=",
            "Bearer ",
            "BEGIN PRIVATE KEY",
            "Owner:",
            "负责人：",
            "@",
            "kubectl ",
            "helm ",
            "docker compose",
            "az deployment"
        })
        {
            Assert.DoesNotContain(forbidden, safetyText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProjectReferences_StayInsideSourceTree_AndGraphIsAcyclic()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(RepositoryRoot, "src")) + Path.DirectorySeparatorChar;
        var projects = LoadProjects();

        foreach (var project in projects.Values)
        {
            foreach (var reference in project.Document.Descendants("ProjectReference"))
            {
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.Path)!, reference.Attribute("Include")!.Value));
                Assert.StartsWith(sourceRoot, resolved, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(resolved), $"Missing project reference: {resolved}");
            }
        }

        foreach (var packageId in projects.Keys)
        {
            Assert.False(HasCycle(packageId, packageId, new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void NuGetConfiguration_MapsPrivatePackagesWithoutCredentials()
    {
        var path = Path.Combine(RepositoryRoot, "NuGet.config");
        var document = XDocument.Load(path);
        var sources = document.Descendants("packageSources").Elements("add")
            .ToDictionary(element => element.Attribute("key")!.Value, element => element.Attribute("value")!.Value);
        var mappings = document.Descendants("packageSource")
            .ToDictionary(
                element => element.Attribute("key")!.Value,
                element => element.Elements("package").Select(pattern => pattern.Attribute("pattern")!.Value).ToArray());

        Assert.Single(document.Descendants("packageSources").Single().Elements("clear"));
        Assert.Equal(["github", "nuget.org"], sources.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["github", "nuget.org"], mappings.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("https://api.nuget.org/v3/index.json", sources["nuget.org"]);
        Assert.Equal("https://nuget.pkg.github.com/GTX537/index.json", sources["github"]);
        Assert.Equal(["CP6.Platform.*"], mappings["github"]);
        Assert.Equal(["*"], mappings["nuget.org"]);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("packageSourceCredentials", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCycle(string origin, string current, HashSet<string> path)
    {
        if (!path.Add(current))
        {
            return current == origin;
        }

        var hasCycle = ExpectedDependencies[current].Any(dependency => HasCycle(origin, dependency, new HashSet<string>(path, StringComparer.Ordinal)));
        return hasCycle;
    }

    private static bool HasDirectorySegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<XElement> GetProjectItems(XDocument document) =>
        document.Descendants()
            .Where(element => string.Equals(element.Parent?.Name.LocalName, "ItemGroup", StringComparison.Ordinal));

    private static string GetItemValue(XElement item, string name) =>
        item.Attribute(name)?.Value
        ?? item.Elements().SingleOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.Ordinal))?.Value
        ?? string.Empty;

    private static IReadOnlyDictionary<string, ProjectInfo> LoadProjects()
    {
        return Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => new ProjectInfo(path, XDocument.Load(path)))
            .ToDictionary(
                project => project.Document.Descendants("PackageId").Single().Value,
                project => project,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CP6.Platform repository root.");
    }

    private sealed record ProjectInfo(string Path, XDocument Document);
}
