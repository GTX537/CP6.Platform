using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Platform.AspNetCore;

public static class Cp6JwtBearerExtensions
{
    /// <summary>
    /// Registers the CP6 fail-closed RS256/JWKS bearer-token profile.
    /// </summary>
    public static AuthenticationBuilder AddCp6JwtBearer(
        this IServiceCollection services,
        Cp6JwtBearerProfile profile,
        string authenticationScheme = JwtBearerDefaults.AuthenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        profile.Validate();
        var audiences = profile.Audiences.ToArray();

        services.AddCp6ProblemDetails();
        var builder = services
            .AddAuthentication(authenticationScheme)
            .AddJwtBearer(authenticationScheme, options =>
            {
                options.Authority = profile.Authority.TrimEnd('/');
                options.RequireHttpsMetadata = profile.RequireHttpsMetadata;
                options.ConfigurationManager = new Cp6DeferredConfigurationManager();
                options.MapInboundClaims = false;
                options.RefreshOnIssuerKeyNotFound = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = profile.Issuer,
                    ValidAudiences = audiences,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = profile.ClockSkew,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidTypes = ["at+jwt"],
                    TryAllIssuerSigningKeys = false,
                    IgnoreTrailingSlashWhenValidatingAudience = false
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = Cp6JwtClaimsValidator.ValidateAsync,
                    OnAuthenticationFailed = context =>
                    {
                        if (!context.HttpContext.RequestAborted.IsCancellationRequested &&
                            IsKnownConfigurationFailure(context.Exception))
                        {
                            context.Fail("Bearer signing-key configuration is unavailable.");
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await context.HttpContext.WriteCp6ProblemAsync(CP6.Platform.Contracts.Cp6Problems.AuthenticationRequired);
                    },
                    OnForbidden = context =>
                        context.HttpContext.WriteCp6ProblemAsync(CP6.Platform.Contracts.Cp6Problems.Forbidden)
                };
            });

        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(serviceProvider =>
            new Cp6JwtBearerPostConfigure(
                authenticationScheme,
                profile,
                serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System));
        return builder;
    }

    private static bool IsKnownConfigurationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is Cp6JwtConfigurationException)
            {
                return true;
            }
        }

        return false;
    }
}
