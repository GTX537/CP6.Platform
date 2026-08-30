using CP6.Platform.Testing;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class Cp6FaultInjectionServiceCollectionExtensions
{
    public static IServiceCollection AddCp6HttpFaultInjection(
        this IServiceCollection services,
        IHostEnvironment environment,
        Cp6HttpFaultScript script)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.EnvironmentName is not ("Test" or "CI"))
        {
            throw new InvalidOperationException("CP6 HTTP fault injection can be registered only in Test or CI.");
        }

        ArgumentNullException.ThrowIfNull(script);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(Cp6HttpFaultScript))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<Cp6HttpFaultScript>()
            .SingleOrDefault();
        if (existing is not null)
        {
            if (!ReferenceEquals(existing, script))
            {
                throw new InvalidOperationException("CP6 HTTP fault injection is already registered with a different script.");
            }

            return services;
        }

        services.AddSingleton(script);
        services.AddTransient(provider => new Cp6HttpFaultHandler(
            script,
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
