using CP6.Platform.AspNetCore.HttpV2;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class Cp6HttpResilienceServiceCollectionExtensions
{
    /// <summary>Opt in one named client. Enabled writes require a host-registered binding and callable authority.</summary>
    public static IHttpClientBuilder AddCp6HttpResilience(this IHttpClientBuilder builder, Cp6HttpV2Profile profile,
        Cp6HttpOperationBinding? binding = null, Func<IServiceProvider, ICp6HttpOperationAuthority>? authorityFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder); ArgumentNullException.ThrowIfNull(profile);
        if (builder.Name != profile.ClientName) throw new ArgumentException("Profile client name does not match.");
        if (binding is not null && (binding.ClientName != builder.Name || binding.PolicyVersion != profile.PolicyVersion))
            throw new ArgumentException("Binding does not match this client and policy version.");
        if (profile.OperationKind == CP6.Platform.AspNetCore.Cp6HttpOperationKind.IdempotentWrite &&
            profile.RetryMode == Cp6HttpRetryMode.OwnerProvenIdempotent &&
            (binding is null || authorityFactory is null || binding.OneTimeCredentialExchange))
            throw new InvalidOperationException("Write retries require an explicit eligible Owner registration; otherwise select Disabled.");
        if (builder.Services.Any(d => d.ImplementationInstance is Cp6HttpResilienceRegistration legacy && legacy.Profile.ClientName == builder.Name))
            throw new InvalidOperationException("Legacy and v2 handlers cannot be stacked.");
        var registration = new Cp6HttpV2Registration(profile, binding, authorityFactory);
        var existing = builder.Services.Select(d => d.ImplementationInstance).OfType<Cp6HttpV2Registration>()
            .SingleOrDefault(r => r.Profile.ClientName == builder.Name);
        if (existing is not null)
        {
            if (existing != registration) throw new InvalidOperationException("Conflicting v2 client registration.");
            return builder;
        }
        builder.Services.AddSingleton(registration);
        builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.ConfigurePrimaryHttpMessageHandler((handler, _) =>
        {
            // Preserve host TLS/proxy configuration while closing hidden replay/credential paths on this opted-in client.
            if (handler is SocketsHttpHandler sockets) { sockets.AllowAutoRedirect = false; sockets.UseCookies = false; }
            else if (handler is HttpClientHandler client) { client.AllowAutoRedirect = false; client.UseCookies = false; }
        });
        builder.AddHttpMessageHandler(provider => new Cp6HttpV2Handler(profile,
            provider.GetService<TimeProvider>() ?? TimeProvider.System, binding, authorityFactory?.Invoke(provider)));
        builder.Services.PostConfigure<HttpClientFactoryOptions>(builder.Name, options =>
        {
            // Runs after all normal named-client configuration, in either registration order.
            options.HttpClientActions.Add(client =>
            {
                if (client.Timeout != Timeout.InfiniteTimeSpan)
                    throw new InvalidOperationException("The v2 deadline must be the only total-timeout authority.");
            });
            options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
            {
                if (handlerBuilder.AdditionalHandlers.Count != 1 || handlerBuilder.AdditionalHandlers[0] is not Cp6HttpV2Handler)
                    throw new InvalidOperationException("The v2 client must have exactly one policy handler; other retry/timeout owners are not allowed.");
                if (handlerBuilder.PrimaryHandler is SocketsHttpHandler { AllowAutoRedirect: true } or SocketsHttpHandler { UseCookies: true }
                    or HttpClientHandler { AllowAutoRedirect: true } or HttpClientHandler { UseCookies: true })
                    throw new InvalidOperationException("The v2 transport cannot redirect or acquire cookie credentials.");
            });
        });
        return builder;
    }
    private sealed record Cp6HttpV2Registration(Cp6HttpV2Profile Profile, Cp6HttpOperationBinding? Binding,
        Func<IServiceProvider, ICp6HttpOperationAuthority>? AuthorityFactory);
}
