using System.Globalization;
using System.Threading.RateLimiting;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace CP6.Platform.AspNetCore;

public static class Cp6GatewayExtensions
{
    private const string PolicyPrefix = "cp6-gateway-";

    /// <summary>
    /// Registers validated in-memory YARP routes, per-source fixed-window limits and identity-header removal.
    /// </summary>
    public static IServiceCollection AddCp6Gateway(this IServiceCollection services, Cp6GatewayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        Cp6GatewayProfileValidator.Validate(profile);

        var routes = profile.Routes.Select(route => new RouteConfig
        {
            RouteId = route.RouteId,
            ClusterId = route.ClusterId,
            Order = route.Order,
            AuthorizationPolicy = route.AuthorizationPolicy,
            RateLimiterPolicy = PolicyName(route.RouteId),
            Match = new RouteMatch
            {
                Path = route.MatchPath,
                Methods = route.Methods.Count == 0 ? null : route.Methods.ToArray()
            }
        }).ToArray();

        var clusters = profile.Clusters.Select(cluster => new ClusterConfig
        {
            ClusterId = cluster.ClusterId,
            Destinations = cluster.Destinations.ToDictionary(
                destination => destination.DestinationId,
                destination => new DestinationConfig { Address = destination.Address.AbsoluteUri },
                StringComparer.Ordinal)
        }).ToArray();

        services.AddCp6ProblemDetails();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.WriteCp6ProblemAsync(Cp6Problems.RateLimitExceeded);
            };

            foreach (var route in profile.Routes)
            {
                var limit = route.RateLimit;
                options.AddPolicy(PolicyName(route.RouteId), httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = limit.PermitLimit,
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            Window = limit.Window
                        }));
            }
        });

        services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddTransforms(builderContext =>
            {
                builderContext.AddRequestTransform(transformContext =>
                {
                    var names = transformContext.ProxyRequest.Headers.Select(header => header.Key).ToArray();
                    foreach (var name in names)
                    {
                        if (Cp6GatewayHeaders.IsUntrustedIdentityHeader(name))
                        {
                            transformContext.ProxyRequest.Headers.Remove(name);
                        }
                    }

                    if (transformContext.ProxyRequest.Content is not null)
                    {
                        names = transformContext.ProxyRequest.Content.Headers.Select(header => header.Key).ToArray();
                        foreach (var name in names)
                        {
                            if (Cp6GatewayHeaders.IsUntrustedIdentityHeader(name))
                            {
                                transformContext.ProxyRequest.Content.Headers.Remove(name);
                            }
                        }
                    }

                    return ValueTask.CompletedTask;
                });
            });

        return services;
    }

    /// <summary>
    /// Adds the rate limiter after routing and before the reverse-proxy endpoints.
    /// </summary>
    public static IApplicationBuilder UseCp6GatewayRateLimiting(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.UseRateLimiter();
    }

    public static IEndpointConventionBuilder MapCp6Gateway(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapReverseProxy();
    }

    private static string PolicyName(string routeId) => $"{PolicyPrefix}{routeId}";
}
