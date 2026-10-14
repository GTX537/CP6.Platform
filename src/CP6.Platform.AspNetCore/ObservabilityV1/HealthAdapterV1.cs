using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Routing.Template;

namespace CP6.Platform.AspNetCore.ObservabilityV1;

public static class HealthAdapterV1
{
    public static IServiceCollection AddCp6HealthV1(this IServiceCollection services, HealthProfileV1 profile, RuntimeIdentityEvaluatorV1 identity)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(identity);
        if (services.Any(s => s.ServiceType == typeof(HealthRuntimeV1))) throw new InvalidOperationException("Health V1 already registered.");
        services.AddHealthChecks(); services.AddSingleton(profile); services.AddSingleton(identity); services.AddSingleton<HealthRuntimeV1>();
        services.AddTransient<IStartupFilter, HealthRouteStartupFilterV1>(); return services;
    }

    public static void MapCp6HealthV1(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetRequiredService<HealthRuntimeV1>();
        runtime.Validate();
        var paths = new[] { "/ops/v1/live", "/ops/v1/startup", "/ops/v1/ready", "/ops/v1/identity" };
        if (endpoints.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Any(e => paths.Any(path => Matches(e, path))))
            throw new InvalidOperationException("An operational route is already registered.");
        endpoints.MapGet(paths[0], (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { contractVersion = "1.0.0", status = "live" });
        }).RequireAuthorization(runtime.Profile.PermissionPolicy);
        endpoints.MapGet(paths[1], (HttpContext context) => Write(context, runtime, HealthPurposeV1.Startup)).RequireAuthorization(runtime.Profile.PermissionPolicy);
        endpoints.MapGet(paths[2], (HttpContext context) => Write(context, runtime, HealthPurposeV1.Ready)).RequireAuthorization(runtime.Profile.PermissionPolicy);
        endpoints.MapGet(paths[3], (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(runtime.Identity.Observe());
        }).RequireAuthorization(runtime.Profile.PermissionPolicy);
        runtime.ValidateCompletedRoutes = () =>
        {
            if (paths.Any(path => endpoints.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Count(e => Matches(e, path)) != 1))
                throw new InvalidOperationException("An operational route collides with the completed route inventory.");
        };
    }

    private static bool Matches(RouteEndpoint endpoint, string path) => new TemplateMatcher(TemplateParser.Parse(endpoint.RoutePattern.RawText ?? "/"), new RouteValueDictionary()).TryMatch(path, new RouteValueDictionary());

    private static async Task Write(HttpContext context, HealthRuntimeV1 runtime, HealthPurposeV1 purpose)
    {
        var result = await runtime.ProbeAsync(purpose, context.RequestAborted);
        context.Response.StatusCode = result.Healthy ? 200 : 503;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(result, context.RequestAborted);
    }
}

public sealed record HealthComponentV1(string Name, bool Healthy, ProbeFailureV1 Failure);
public sealed record HealthRouteObservationV1(string Template, CapabilityV1 Capability, bool Available, IdentityVerdictV1 RuntimeIdentity, ProbeFailureV1 DependencyFailure);
public sealed record HealthObservationV1(bool Healthy, bool ReadAvailable, bool NewEffectsAvailable, IdentityVerdictV1 RuntimeIdentity,
    IReadOnlyList<HealthComponentV1> Components, IReadOnlyList<HealthRouteObservationV1> Routes)
{
    public string ContractVersion => "1.0.0";
    public string Scope => "local-nonproduction";
}

internal sealed class HealthRuntimeV1(HealthProfileV1 profile, RuntimeIdentityEvaluatorV1 identity, IServiceScopeFactory scopes,
    IOptions<HealthCheckServiceOptions> options, IAuthorizationPolicyProvider authorization, TimeProvider? timeProvider = null)
{
    // Process-wide bound includes probes abandoned by disposed hosts. Slots are released only when the delegate returns.
    private static readonly SemaphoreSlim ProcessSlots = new(8, 8);
    private readonly Dictionary<string, ProbeState> state = profile.Checks.ToDictionary(x => x.RegistrationName, _ => new ProbeState());
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    internal HealthProfileV1 Profile => profile;
    internal RuntimeIdentityEvaluatorV1 Identity => identity;
    internal Action ValidateCompletedRoutes { get; set; } = () => throw new InvalidOperationException("Health V1 routes were not mapped.");

    internal void Validate()
    {
        if (authorization.GetPolicyAsync(profile.PermissionPolicy).GetAwaiter().GetResult() is null) throw new InvalidOperationException("Missing health permission policy.");
        foreach (var binding in profile.Checks)
        {
            var matches = options.Value.Registrations.Where(r => r.Name == binding.RegistrationName).ToArray();
            if (matches.Length != 1 || !matches[0].Tags.Contains(binding.RequiredTag)) throw new InvalidOperationException("Health check inventory does not match registrations.");
        }
    }

    internal async Task<HealthObservationV1> ProbeAsync(HealthPurposeV1 purpose, CancellationToken cancellationToken)
    {
        var bindings = profile.Checks.Where(b => b.Purpose == purpose).ToArray();
        var results = await Task.WhenAll(bindings.Select(b => ProbeOne(b, cancellationToken)));
        var healthy = results.Length > 0 && results.All(r => r.Healthy) && !cancellationToken.IsCancellationRequested;
        var observation = identity.Observe(cancellationToken);
        var verdict = observation.Verdict;
        var published = Array.AsReadOnly(results.Where((_, index) => bindings[index].Publish).ToArray());
        if (purpose == HealthPurposeV1.Startup) return new(healthy, false, false, verdict, published, Array.Empty<HealthRouteObservationV1>());
        var byRegistration = bindings.Select((binding, index) => (binding.RegistrationName, Result: results[index]))
            .ToDictionary(pair => pair.RegistrationName, pair => pair.Result, StringComparer.Ordinal);
        var routes = profile.Routes.Select(route =>
        {
            var failure = route.RequiredChecks.Select(name => byRegistration.TryGetValue(name, out var result) ? result.Failure : ProbeFailureV1.Missing)
                .FirstOrDefault(value => value != ProbeFailureV1.None);
            var dependenciesHealthy = route.RequiredChecks.Count > 0 && route.RequiredChecks.All(name => byRegistration.TryGetValue(name, out var result) && result.Healthy);
            var routeIdentity = route.Capability == CapabilityV1.NewEffect
                ? observation.ModuleVerdicts.GetValueOrDefault(route.RequiredModule!, verdict == IdentityVerdictV1.Unproven ? IdentityVerdictV1.Unproven : IdentityVerdictV1.Mismatch)
                : IdentityVerdictV1.Unproven;
            return new HealthRouteObservationV1(route.Template, route.Capability,
                dependenciesHealthy && !cancellationToken.IsCancellationRequested && (route.Capability == CapabilityV1.Read || routeIdentity == IdentityVerdictV1.Match), routeIdentity, failure);
        }).ToArray();
        var reads = routes.Any(route => route.Capability == CapabilityV1.Read && route.Available);
        var effects = routes.Any(route => route.Capability == CapabilityV1.NewEffect && route.Available);
        var hasEffects = routes.Any(route => route.Capability == CapabilityV1.NewEffect);
        return new(healthy && routes.Length > 0 && routes.All(route => route.Available) && (!hasEffects || verdict == IdentityVerdictV1.Match),
            reads, effects, verdict, published, Array.AsReadOnly(routes));
    }

    private async Task<HealthComponentV1> ProbeOne(HealthCheckBindingV1 binding, CancellationToken cancellationToken)
    {
        var slot = state[binding.RegistrationName]; Task<HealthComponentV1> pending;
        if (cancellationToken.IsCancellationRequested) return new(binding.SafeName, false, ProbeFailureV1.Cancelled);
        lock (slot)
        {
            if (slot.Cached is not null)
            {
                var age = clock.GetElapsedTime(slot.CompletedTimestamp);
                if (age >= TimeSpan.Zero && age < binding.Freshness) return slot.Cached;
            }
            if (slot.Running is { IsCompleted: false }) return new(binding.SafeName, false, ProbeFailureV1.Busy);
            if (!ProcessSlots.Wait(0)) return new(binding.SafeName, false, ProbeFailureV1.Busy);
            // Even a synchronously blocking factory/check runs outside the HTTP request; globally bounded.
            pending = Task.Run(async () =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(binding.Timeout);
                    using var scope = scopes.CreateScope();
                    var registration = options.Value.Registrations.Single(r => r.Name == binding.RegistrationName && r.Tags.Contains(binding.RequiredTag));
                    var check = registration.Factory(scope.ServiceProvider);
                    var result = await check.CheckHealthAsync(new HealthCheckContext { Registration = registration }, timeout.Token);
                    return new HealthComponentV1(binding.SafeName, result.Status == HealthStatus.Healthy && !timeout.IsCancellationRequested,
                        timeout.IsCancellationRequested ? ProbeFailureV1.TimedOut : result.Status == HealthStatus.Healthy ? ProbeFailureV1.None : ProbeFailureV1.Failed);
                }
                catch (OperationCanceledException) { return new(binding.SafeName, false, ProbeFailureV1.Cancelled); }
                catch { return new(binding.SafeName, false, ProbeFailureV1.Failed); }
                finally { ProcessSlots.Release(); }
            });
            slot.Running = pending;
        }
        HealthComponentV1 observed;
        try { observed = await pending.WaitAsync(binding.Timeout, cancellationToken); }
        catch (TimeoutException) { observed = new(binding.SafeName, false, ProbeFailureV1.TimedOut); }
        catch (OperationCanceledException) { observed = new(binding.SafeName, false, ProbeFailureV1.Cancelled); }
        lock (slot) { slot.Cached = observed; slot.CompletedTimestamp = clock.GetTimestamp(); }
        return observed;
    }
    private sealed class ProbeState
    {
        internal Task<HealthComponentV1>? Running;
        internal HealthComponentV1? Cached;
        internal long CompletedTimestamp;
    }
}

internal sealed class HealthRouteStartupFilterV1(HealthRuntimeV1 runtime) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    { runtime.ValidateCompletedRoutes(); next(application); };
}
