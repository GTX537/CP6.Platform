using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCore.HttpV2;

public enum Cp6SafeProblemWriteResult { Written, GenericFallback, ResponseStarted, RequestAborted, PolicyNotBound }

public static class Cp6SafeProblemWriter
{
    /// <summary>Input values are message codes only. No exception text, arbitrary strings, or hidden fields are echoed.</summary>
    public static async Task<Cp6SafeProblemWriteResult> WriteAsync(HttpContext context, Cp6SafeProblemPolicy policy,
        IReadOnlyDictionary<string, string[]>? errorCodes = null)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(policy);
        if (context.RequestAborted.IsCancellationRequested) return Cp6SafeProblemWriteResult.RequestAborted;
        if (context.Response.HasStarted) return Cp6SafeProblemWriteResult.ResponseStarted;
        if (!ReferenceEquals(context.GetEndpoint()?.Metadata.GetMetadata<Cp6SafeProblemPolicy>(), policy) ||
            !context.RequestServices.GetServices<Cp6SafeProblemPolicy>().Any(p => ReferenceEquals(p, policy)))
            return Cp6SafeProblemWriteResult.PolicyNotBound;
        var trace = Activity.Current?.TraceId.ToHexString();
        if (trace is null || trace.Length != 32 || trace.All(c => c == '0') || trace.Any(c => !Uri.IsHexDigit(c)))
            trace = ActivityTraceId.CreateRandom().ToHexString();
        var correlation = context.TraceIdentifier;
        if (!Cp6HttpOperationIdentity.IsSafeKey(correlation)) correlation = Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlation;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var fallback = errorCodes?.Count > 32;
        if (!fallback && errorCodes is not null)
        {
            foreach (var field in errorCodes)
            {
                if (!policy.ContainsField(field.Key) || field.Value is null || field.Value.Length > 4) { fallback = true; break; }
                var messages = new List<string>();
                foreach (var code in field.Value)
                {
                    if (code is null || !policy.TryResolve(field.Key, code, out var message) || message.Length > 256)
                    { fallback = true; break; }
                    messages.Add(message);
                }
                if (fallback) break;
                errors.Add(field.Key, messages.ToArray());
            }
        }
        var definition = policy.Definition;
        var payload = new Dictionary<string, object?>
        {
            ["type"] = definition.Type, ["title"] = definition.Title, ["status"] = definition.Status,
            ["code"] = definition.Code, ["messageKey"] = definition.MessageKey,
            ["traceId"] = trace, ["correlationId"] = correlation
        };
        if (errorCodes is not null) payload["errors"] = errors;
        var bytes = fallback ? [] : JsonSerializer.SerializeToUtf8Bytes(payload);
        if (fallback || bytes.Length > 16384)
        {
            fallback = true;
            bytes = JsonSerializer.SerializeToUtf8Bytes(new { code = "CP6_SAFE_ERROR", correlationId = correlation, traceId = trace });
        }
        if (context.RequestAborted.IsCancellationRequested) return Cp6SafeProblemWriteResult.RequestAborted;
        if (context.Response.HasStarted) return Cp6SafeProblemWriteResult.ResponseStarted;
        context.Response.StatusCode = definition.Status;
        context.Response.ContentType = "application/problem+json";
        context.Response.ContentLength = bytes.Length;
        try { await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { return Cp6SafeProblemWriteResult.RequestAborted; }
        return fallback ? Cp6SafeProblemWriteResult.GenericFallback : Cp6SafeProblemWriteResult.Written;
    }
}
