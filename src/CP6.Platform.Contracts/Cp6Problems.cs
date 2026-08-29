namespace CP6.Platform.Contracts;

/// <summary>
/// Stable Platform-owned problem definitions. Business services retain their own code registries.
/// </summary>
public static class Cp6Problems
{
    public static Cp6ProblemDefinition AuthenticationRequired { get; } = new(
        "https://errors.cp6.uk/platform/authentication-required",
        "Authentication is required",
        401,
        "CP6_AUTHENTICATION_REQUIRED",
        "cp6.error.authenticationRequired");

    public static Cp6ProblemDefinition Forbidden { get; } = new(
        "https://errors.cp6.uk/platform/forbidden",
        "The request is forbidden",
        403,
        "CP6_FORBIDDEN",
        "cp6.error.forbidden");

    public static Cp6ProblemDefinition RateLimitExceeded { get; } = new(
        "https://errors.cp6.uk/platform/rate-limit-exceeded",
        "The request rate limit was exceeded",
        429,
        "CP6_RATE_LIMIT_EXCEEDED",
        "cp6.error.rateLimitExceeded");
}
