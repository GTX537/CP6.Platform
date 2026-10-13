using CP6.Platform.Abstractions;
using CP6.Platform.Abstractions.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CP6.Platform.AspNetCore;

public static class RequestContextExtensions
{
    /// <summary>
    /// Registers the read-only request context boundary and a service-owned trusted resolver.
    /// </summary>
    public static IServiceCollection AddCp6RequestContext<TResolver>(this IServiceCollection services)
        where TResolver : class, IRequestContextResolver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ContextScopeAccessorV1>();
        services.TryAddScoped<RequestContextAccessor>();
        services.AddScoped<IRequestContextAccessor>(provider => provider.GetRequiredService<RequestContextAccessor>());
        services.TryAddScoped<ITrustedContextAccessorV1>(provider => provider.GetRequiredService<ContextScopeAccessorV1>());
        services.AddScoped<IRequestContextResolver, TResolver>();
        return services;
    }

    /// <summary>
    /// Adds the fail-closed CP6 request context middleware.
    /// </summary>
    public static IApplicationBuilder UseCp6RequestContext(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.UseMiddleware<RequestContextMiddleware>();
    }
}
