using CP6.Platform.Abstractions;
using CP6.Platform.Abstractions.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CP6.Platform.AspNetCore.Context;

public static class TrustedRequestContextExtensionsV1
{
    /// <summary>Opt-in only. The host supplies actual authentication and current authority adapters.</summary>
    public static IServiceCollection AddCp6TrustedRequestContextV1<TVerifier, TAuthority>(this IServiceCollection services)
        where TVerifier : class, IContextSourceVerifierV1<HttpContext>
        where TAuthority : class, IContextBindingAuthorityV1
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ContextScopeAccessorV1>();
        services.TryAddScoped<RequestContextAccessor>();
        services.AddScoped<IRequestContextAccessor>(sp => sp.GetRequiredService<RequestContextAccessor>());
        services.TryAddScoped<ITrustedContextAccessorV1>(sp => sp.GetRequiredService<ContextScopeAccessorV1>());
        services.AddScoped<IContextSourceVerifierV1<HttpContext>, TVerifier>();
        services.AddScoped<IContextBindingAuthorityV1, TAuthority>();
        services.AddScoped<TrustedContextExecutorV1>();
        return services;
    }

    public static IApplicationBuilder UseCp6TrustedRequestContextV1(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.UseMiddleware<TrustedRequestContextMiddlewareV1>();
    }
}
