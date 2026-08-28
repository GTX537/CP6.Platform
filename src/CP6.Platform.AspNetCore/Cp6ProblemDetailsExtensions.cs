using System.Diagnostics;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCore;

public static class Cp6ProblemDetailsExtensions
{
    /// <summary>
    /// Registers the RFC 9457 writer used by CP6 authentication and service errors.
    /// </summary>
    public static IServiceCollection AddCp6ProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddProblemDetails();
        return services;
    }

    /// <summary>
    /// Establishes the safe correlation identifier before authentication and request context.
    /// </summary>
    public static IApplicationBuilder UseCp6Correlation(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.UseMiddleware<Cp6CorrelationMiddleware>();
    }

    /// <summary>
    /// Writes a CP6 RFC 9457 response without exposing exception, token or request payload data.
    /// </summary>
    public static async Task WriteCp6ProblemAsync(
        this HttpContext httpContext,
        Cp6ProblemDefinition definition,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(definition);

        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.StatusCode = definition.Status;
        httpContext.Response.ContentType = "application/problem+json";

        var traceId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId) || traceId.Length != 32)
        {
            traceId = ActivityTraceId.CreateRandom().ToString();
        }

        var correlationId = httpContext.TraceIdentifier;
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
            httpContext.TraceIdentifier = correlationId;
        }

        var problem = new ProblemDetails
        {
            Type = definition.Type,
            Title = definition.Title,
            Status = definition.Status
        };
        problem.Extensions["code"] = definition.Code;
        problem.Extensions["messageKey"] = definition.MessageKey;
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        var service = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (service is not null && await service.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        }))
        {
            return;
        }

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }
}
