using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CP6.Platform.AspNetCoreTests;

[Collection(nameof(OpenTelemetryGlobalStateCollection))]
public sealed class ObservabilityRegistrationTests
{
    [Fact]
    public void AddCp6Observability_AcceptsCandidateAndExposesReleaseIdentity()
    {
        var services = new ServiceCollection();
        var profile = CandidateProfile("crm-api");

        services.AddCp6Observability(profile);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            profile.ReleaseIdentity,
            provider.GetRequiredService<ICp6ReleaseIdentityAccessor>().Current);
    }

    [Fact]
    public void AddCp6Observability_AcceptsNonCandidateWithoutImmutableDigests()
    {
        var services = new ServiceCollection();
        var profile = NonCandidateProfile();

        services.AddCp6Observability(profile);
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<ICp6ReleaseIdentityAccessor>().Current.Candidate);
    }

    [Fact]
    public void AddCp6Observability_RejectsMissingCandidateIdentity()
    {
        var release = new Cp6ReleaseIdentity(
            "crm-api",
            "0.8.0-alpha.1",
            string.Empty,
            string.Empty,
            string.Empty,
            Cp6ReleaseMode.Candidate);
        var profile = new Cp6ObservabilityProfile("crm-api", "0.8.0-alpha.1", "test", "us-east", release);

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCp6Observability(profile));
    }

    [Fact]
    public void AddCp6Observability_RejectsInvalidCandidateIdentity()
    {
        var release = new Cp6ReleaseIdentity(
            "crm-api",
            "0.8.0-alpha.1",
            new string('a', 40),
            "sha256:not-a-digest",
            "sha256:" + new string('c', 64),
            Cp6ReleaseMode.Candidate);
        var profile = new Cp6ObservabilityProfile("crm-api", "0.8.0-alpha.1", "test", "us-east", release);

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCp6Observability(profile));
    }

    [Fact]
    public void AddCp6Observability_RejectsProfileReleaseMismatch()
    {
        var baseline = CandidateProfile("crm-api");
        var profile = new Cp6ObservabilityProfile(
            "portal-api",
            baseline.ServiceVersion,
            baseline.EnvironmentName,
            baseline.Region,
            baseline.ReleaseIdentity);

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCp6Observability(profile));
    }

    [Theory]
    [InlineData("Production", "us-east")]
    [InlineData("test", "US-EAST")]
    [InlineData("test", "region-name-that-is-longer-than-32")]
    public void AddCp6Observability_RejectsNonCanonicalDeploymentDimensions(
        string environmentName,
        string region)
    {
        var baseline = CandidateProfile("crm-api");
        var profile = baseline with { EnvironmentName = environmentName, Region = region };

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCp6Observability(profile));
    }

    [Fact]
    public void AddCp6Observability_IsIdempotentForEqualProfile()
    {
        var services = new ServiceCollection();
        var profile = CandidateProfile("crm-api");

        services.AddCp6Observability(profile);
        var descriptorCount = services.Count;
        services.AddCp6Observability(profile);

        Assert.Equal(descriptorCount, services.Count);
    }

    [Fact]
    public void AddCp6Observability_RejectsProfileDrift()
    {
        var services = new ServiceCollection();
        services.AddCp6Observability(CandidateProfile("service-a"));

        Assert.Throws<InvalidOperationException>(
            () => services.AddCp6Observability(CandidateProfile("service-b")));
    }

    [Fact]
    public void AddCp6Observability_ProvidesRequiredResourceAttributes()
    {
        var services = new ServiceCollection();
        services.AddCp6Observability(CandidateProfile("crm-api"));
        using var provider = services.BuildServiceProvider();
        var attributes = provider.GetRequiredService<TracerProvider>()
            .GetResource()
            .Attributes
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

        Assert.Equal("crm-api", attributes["service.name"]);
        Assert.Equal("0.8.0-alpha.1", attributes["service.version"]);
        Assert.Equal("test", attributes["deployment.environment.name"]);
        Assert.Equal("us-east", attributes["cp6.region"]);
        Assert.Equal(new string('a', 40), attributes["cp6.release.git_sha"]);
        Assert.Equal("sha256:" + new string('b', 64), attributes["cp6.release.artifact_digest"]);
        Assert.Equal("sha256:" + new string('c', 64), attributes["cp6.release.contract_bundle_digest"]);
        Assert.Equal(true, attributes["cp6.release.candidate"]);
    }

    [Fact]
    public void AddCp6Observability_OmitsEmptyNonCandidateIdentityAttributes()
    {
        var services = new ServiceCollection();
        services.AddCp6Observability(NonCandidateProfile());
        using var provider = services.BuildServiceProvider();
        var attributes = provider.GetRequiredService<TracerProvider>()
            .GetResource()
            .Attributes
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

        Assert.DoesNotContain("cp6.release.git_sha", attributes.Keys);
        Assert.DoesNotContain("cp6.release.artifact_digest", attributes.Keys);
        Assert.DoesNotContain("cp6.release.contract_bundle_digest", attributes.Keys);
        Assert.Equal(false, attributes["cp6.release.candidate"]);
    }

    [Fact]
    public void AddCp6Observability_SubscribesOnlyToStableCp6ActivitySources()
    {
        var services = new ServiceCollection();
        services.AddCp6Observability(CandidateProfile("crm-api"));
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<TracerProvider>();

        foreach (var sourceName in Cp6TelemetrySources.All)
        {
            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("contract-test");
            Assert.NotNull(activity);
        }

        using var unknownSource = new ActivitySource("CP6.Platform.Unknown");
        Assert.Null(unknownSource.StartActivity("contract-test"));
    }

    [Fact]
    public void AddCp6Observability_SubscribesOnlyToStableCp6Meters()
    {
        var exporter = new CapturingMetricExporter();
        var services = new ServiceCollection();
        services.AddCp6Observability(CandidateProfile("crm-api"));
        services.AddOpenTelemetry().WithMetrics(
            metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter)));
        using var provider = services.BuildServiceProvider();
        var meterProvider = provider.GetRequiredService<MeterProvider>();

        foreach (var meterName in Cp6TelemetryMeters.All.Append("CP6.Platform.Unknown"))
        {
            using var meter = new Meter(meterName);
            meter.CreateCounter<long>("contract_test_total").Add(1);
        }

        Assert.True(meterProvider.ForceFlush());
        Assert.Equal(
            Cp6TelemetryMeters.All.OrderBy(name => name, StringComparer.Ordinal),
            exporter.MeterNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void AddCp6Observability_UsesTraceContextWithoutBaggage()
    {
        new ServiceCollection().AddCp6Observability(CandidateProfile("crm-api"));

        var propagator = Assert.IsType<TraceContextPropagator>(Propagators.DefaultTextMapPropagator);
        Assert.Equal(
            new[] { "traceparent", "tracestate" },
            propagator.Fields.OrderBy(field => field, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "traceparent", "tracestate" },
            DistributedContextPropagator.Current.Fields.OrderBy(field => field, StringComparer.Ordinal));
    }

    [Fact]
    public void AddCp6Observability_DoesNotRegisterAnExporter()
    {
        var services = new ServiceCollection();

        services.AddCp6Observability(CandidateProfile("crm-api"));

        Assert.DoesNotContain(
            services,
            descriptor =>
                ContainsExporterName(descriptor.ServiceType) ||
                ContainsExporterName(descriptor.ImplementationType));
    }

    [Fact]
    public async Task AddCp6Observability_AllowsBusinessRequestWithoutExporter()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddCp6Observability(CandidateProfile("crm-api"));
        await using var application = builder.Build();
        application.MapGet("/business", () => Results.Ok(new { status = "accepted" }));
        await application.StartAsync();
        var addresses = application.Services.GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses ?? [])) };

        using var response = await client.GetAsync("/business");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await application.StopAsync();
    }

    private static bool ContainsExporterName(Type? type) =>
        type?.FullName?.Contains("Exporter", StringComparison.Ordinal) == true;

    private static Cp6ObservabilityProfile CandidateProfile(string serviceName)
    {
        const string version = "0.8.0-alpha.1";
        var release = new Cp6ReleaseIdentity(
            serviceName,
            version,
            new string('a', 40),
            "sha256:" + new string('b', 64),
            "sha256:" + new string('c', 64),
            Cp6ReleaseMode.Candidate);
        return new Cp6ObservabilityProfile(serviceName, version, "test", "us-east", release);
    }

    private static Cp6ObservabilityProfile NonCandidateProfile()
    {
        var release = new Cp6ReleaseIdentity(
            "crm-api",
            "local",
            string.Empty,
            string.Empty,
            string.Empty,
            Cp6ReleaseMode.NonCandidate);
        return new Cp6ObservabilityProfile("crm-api", "local", "local", "local-dev", release);
    }

    private sealed class CapturingMetricExporter : BaseExporter<Metric>
    {
        private readonly HashSet<string> meterNames = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> MeterNames => meterNames;

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                meterNames.Add(metric.MeterName);
            }

            return ExportResult.Success;
        }
    }
}

[CollectionDefinition(nameof(OpenTelemetryGlobalStateCollection), DisableParallelization = true)]
public sealed class OpenTelemetryGlobalStateCollection;
