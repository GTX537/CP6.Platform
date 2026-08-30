using System.Net;
using System.Text.RegularExpressions;
using CP6.Platform.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class Cp6HttpResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Adds bounded timeout, retry and circuit behavior to one explicitly classified named client.
    /// </summary>
    public static IHttpClientBuilder AddCp6HttpResilience(
        this IHttpClientBuilder builder,
        Cp6HttpResilienceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(builder.Name, profile.ClientName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Profile client name must match the named HTTP client.", nameof(profile));
        }

        var existing = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(Cp6HttpResilienceRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<Cp6HttpResilienceRegistration>()
            .SingleOrDefault(registration =>
                string.Equals(registration.Profile.ClientName, profile.ClientName, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.Profile != profile)
            {
                throw new InvalidOperationException(
                    "CP6 HTTP resilience is already registered with a different profile.");
            }

            return builder;
        }

        builder.Services.AddSingleton(new Cp6HttpResilienceRegistration(profile));
        builder.Services.AddHttpContextAccessor();
        builder.AddHttpMessageHandler(_ => new Cp6HttpRequestValidationHandler(profile));
        builder.AddHttpMessageHandler(provider =>
            new Cp6OutboundCorrelationHandler(provider.GetRequiredService<IHttpContextAccessor>()));
        builder.AddHttpMessageHandler(_ => new Cp6HttpFailureMappingHandler(profile));
        builder.AddResilienceHandler("cp6", (pipeline, context) =>
        {
            pipeline.TimeProvider = context.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Name = "cp6-total-timeout",
                Timeout = profile.TotalTimeout
            });
            if (profile.RetryAttempts > 0)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "cp6-retry",
                    MaxRetryAttempts = profile.RetryAttempts,
                    Delay = TimeSpan.Zero,
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = false,
                    ShouldRetryAfterHeader = false,
                    ShouldHandle = arguments => ValueTask.FromResult(IsApprovedTransient(arguments.Outcome))
                });
            }

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                Name = "cp6-circuit",
                FailureRatio = 0.5,
                SamplingDuration = profile.CircuitSamplingDuration,
                MinimumThroughput = profile.CircuitMinimumThroughput,
                BreakDuration = profile.CircuitBreakDuration,
                ShouldHandle = arguments => ValueTask.FromResult(IsApprovedTransient(arguments.Outcome))
            });
            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Name = "cp6-attempt-timeout",
                Timeout = profile.AttemptTimeout
            });
        });
        return builder;
    }

    private static bool IsApprovedTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException)
        {
            return true;
        }

        return outcome.Result is { } response && response.StatusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    private sealed class Cp6HttpRequestValidationHandler(Cp6HttpResilienceProfile profile) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request, profile.OperationKind);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Cp6HttpFailureMappingHandler(Cp6HttpResilienceProfile profile) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (TimeoutRejectedException exception)
            {
                var category = exception.Timeout == profile.TotalTimeout
                    ? Cp6HttpFailureCategory.TotalTimeout
                    : Cp6HttpFailureCategory.AttemptTimeout;
                throw new Cp6HttpResilienceException(category, exception);
            }
            catch (BrokenCircuitException exception)
            {
                throw new Cp6HttpResilienceException(Cp6HttpFailureCategory.CircuitOpen, exception);
            }
        }
    }

    private static void ValidateRequest(HttpRequestMessage request, Cp6HttpOperationKind operationKind)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (operationKind == Cp6HttpOperationKind.IdempotentRead &&
            request.Method != HttpMethod.Get &&
            request.Method != HttpMethod.Head &&
            request.Method != HttpMethod.Options)
        {
            throw new Cp6HttpResilienceException(Cp6HttpFailureCategory.OperationNotAllowed);
        }

        if (operationKind != Cp6HttpOperationKind.IdempotentRead && !IsWriteMethod(request.Method))
        {
            throw new Cp6HttpResilienceException(Cp6HttpFailureCategory.OperationNotAllowed);
        }

        if (operationKind != Cp6HttpOperationKind.IdempotentWrite)
        {
            return;
        }

        var values = request.Headers.TryGetValues("Idempotency-Key", out var supplied)
            ? supplied.ToArray()
            : [];
        if (values.Length != 1 || !IdempotencyKeyPattern().IsMatch(values[0]))
        {
            throw new Cp6HttpResilienceException(Cp6HttpFailureCategory.IdempotencyRequired);
        }
    }

    private static bool IsWriteMethod(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();

    private sealed record Cp6HttpResilienceRegistration(Cp6HttpResilienceProfile Profile);
}
