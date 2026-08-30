using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Routing;

public static class Cp6OperationalEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps distinct liveness, startup, readiness and immutable release identity endpoints.
    /// </summary>
    public static IEndpointConventionBuilder MapCp6OperationalEndpoints(
        this IEndpointRouteBuilder endpoints,
        Cp6OperationalEndpointProfile profile)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(profile);

        var existing = endpoints.DataSources
            .OfType<Cp6OperationalEndpointRegistrationDataSource>()
            .SingleOrDefault();
        if (existing is not null)
        {
            if (!existing.Profile.IsEquivalentTo(profile))
            {
                throw new InvalidOperationException(
                    "CP6 operational endpoints are already mapped with a different profile.");
            }

            return existing.ConventionBuilder;
        }

        var builders = new IEndpointConventionBuilder[]
        {
            endpoints.MapGet(profile.LivePath, Cp6SafeHealthResponseWriter.WriteLiveAsync),
            endpoints.MapHealthChecks(
                profile.StartupPath,
                HealthOptions(Cp6HealthTags.Startup, profile.PublishedComponentNames)),
            endpoints.MapHealthChecks(
                profile.ReadyPath,
                HealthOptions(Cp6HealthTags.Ready, profile.PublishedComponentNames)),
            endpoints.MapGet(profile.ReleasePath, Cp6SafeHealthResponseWriter.WriteReleaseAsync)
        };
        var conventionBuilder = new CompositeEndpointConventionBuilder(builders);
        endpoints.DataSources.Add(
            new Cp6OperationalEndpointRegistrationDataSource(profile, conventionBuilder));
        return conventionBuilder;
    }

    private static HealthCheckOptions HealthOptions(
        string tag,
        IReadOnlySet<string> publishedComponentNames) => new()
        {
            Predicate = registration => registration.Tags.Contains(tag),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = (context, report) =>
                Cp6SafeHealthResponseWriter.WriteHealthAsync(context, report, publishedComponentNames)
        };

    private sealed class CompositeEndpointConventionBuilder(
        IReadOnlyList<IEndpointConventionBuilder> builders) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in builders)
            {
                builder.Add(convention);
            }
        }
    }

    private sealed class Cp6OperationalEndpointRegistrationDataSource(
        Cp6OperationalEndpointProfile profile,
        IEndpointConventionBuilder conventionBuilder) : EndpointDataSource
    {
        private static readonly IChangeToken NeverChanges = new CancellationChangeToken(CancellationToken.None);

        public Cp6OperationalEndpointProfile Profile { get; } = profile;

        public IEndpointConventionBuilder ConventionBuilder { get; } = conventionBuilder;

        public override IReadOnlyList<Endpoint> Endpoints { get; } = [];

        public override IChangeToken GetChangeToken() => NeverChanges;
    }
}
