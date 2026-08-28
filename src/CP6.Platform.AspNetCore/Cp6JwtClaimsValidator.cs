using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CP6.Platform.AspNetCore;

internal static class Cp6JwtClaimsValidator
{
    private static readonly string[] RequiredClaims = ["iss", "aud", "sub", "tenant_id", "jti", "iat", "nbf", "exp"];

    public static Task ValidateAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SecurityToken is not JsonWebToken token ||
            !string.Equals(token.Alg, "RS256", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(token.Kid))
        {
            context.Fail("The token must use RS256 and include a non-empty kid header.");
            return Task.CompletedTask;
        }

        var identity = context.Principal?.Identity as ClaimsIdentity;
        if (identity is null)
        {
            context.Fail("The validated token did not produce a claims identity.");
            return Task.CompletedTask;
        }

        foreach (var claimType in RequiredClaims)
        {
            var values = identity.FindAll(claimType).Select(claim => claim.Value).ToArray();
            if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
            {
                context.Fail($"Required claim '{claimType}' is missing or empty.");
                return Task.CompletedTask;
            }
        }

        foreach (var claimType in new[] { "iss", "sub", "tenant_id", "jti", "iat", "nbf", "exp" })
        {
            if (identity.FindAll(claimType).Skip(1).Any())
            {
                context.Fail($"Required claim '{claimType}' must occur exactly once.");
                return Task.CompletedTask;
            }
        }

        var tenant = identity.FindFirst("tenant_id")!.Value;
        if (!Guid.TryParse(tenant, out var tenantId) || tenantId == Guid.Empty)
        {
            context.Fail("The tenant_id claim must be a non-empty UUID.");
            return Task.CompletedTask;
        }

        if (!TryReadNumericDate(identity, "iat", out var issuedAt) ||
            !TryReadNumericDate(identity, "nbf", out var notBefore) ||
            !TryReadNumericDate(identity, "exp", out var expires) ||
            issuedAt > expires ||
            notBefore > expires)
        {
            context.Fail("iat, nbf and exp must be valid ordered NumericDate values.");
        }

        return Task.CompletedTask;
    }

    private static bool TryReadNumericDate(ClaimsIdentity identity, string type, out long value) =>
        long.TryParse(identity.FindFirst(type)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
