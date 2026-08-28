using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
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
        return services
            .AddAuthentication(authenticationScheme)
            .AddJwtBearer(authenticationScheme, options =>
            {
                options.Authority = profile.Authority.TrimEnd('/');
                options.RequireHttpsMetadata = profile.RequireHttpsMetadata;
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
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = Cp6JwtClaimsValidator.ValidateAsync,
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await context.HttpContext.WriteCp6ProblemAsync(CP6.Platform.Contracts.Cp6Problems.AuthenticationRequired);
                    },
                    OnForbidden = context =>
                        context.HttpContext.WriteCp6ProblemAsync(CP6.Platform.Contracts.Cp6Problems.Forbidden)
                };
            });
    }
}
