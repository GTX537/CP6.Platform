using System.Diagnostics;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

public static class Cp6ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Composes exporter-neutral OpenTelemetry providers for the stable CP6 telemetry contract.
    /// </summary>
    public static IServiceCollection AddCp6Observability(
        this IServiceCollection services,
        Cp6ObservabilityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();

        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(Cp6ObservabilityRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<Cp6ObservabilityRegistration>()
            .SingleOrDefault();

        if (existing is not null)
        {
            if (existing.Profile != profile)
            {
                throw new InvalidOperationException(
                    "CP6 observability is already registered with a different profile.");
            }

            return services;
        }

        services.AddSingleton(new Cp6ObservabilityRegistration(profile));
        services.AddSingleton<ICp6ReleaseIdentityAccessor>(
            new Cp6ReleaseIdentityAccessor(profile.ReleaseIdentity));

        DistributedContextPropagator.Current = Cp6TraceContextDistributedPropagator.Instance;
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

        var resourceAttributes = BuildResourceAttributes(profile);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(profile.ServiceName, serviceVersion: profile.ServiceVersion)
                .AddAttributes(resourceAttributes))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource(Cp6TelemetrySources.All.ToArray()))
            .WithMetrics(metrics => metrics.AddMeter(Cp6TelemetryMeters.All.ToArray()));

        return services;
    }

    private static IReadOnlyDictionary<string, object> BuildResourceAttributes(Cp6ObservabilityProfile profile)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["deployment.environment.name"] = profile.EnvironmentName,
            [Cp6TelemetryConventions.RegionTag] = profile.Region,
            ["cp6.release.candidate"] = profile.ReleaseIdentity.Candidate
        };

        AddWhenPresent(attributes, "cp6.release.git_sha", profile.ReleaseIdentity.GitSha);
        AddWhenPresent(attributes, "cp6.release.artifact_digest", profile.ReleaseIdentity.ArtifactDigest);
        AddWhenPresent(
            attributes,
            "cp6.release.contract_bundle_digest",
            profile.ReleaseIdentity.ContractBundleDigest);
        return attributes;
    }

    private static void AddWhenPresent(IDictionary<string, object> attributes, string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            attributes.Add(name, value);
        }
    }

}
