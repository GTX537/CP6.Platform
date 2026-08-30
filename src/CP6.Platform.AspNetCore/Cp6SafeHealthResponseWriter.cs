using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CP6.Platform.AspNetCore;

internal static class Cp6SafeHealthResponseWriter
{
    private const string SchemaVersion = "1.0.0";

    internal static async Task WriteLiveAsync(HttpContext context)
    {
        PrepareResponse(context, StatusCodes.Status200OK);
        await using var writer = CreateWriter(context);
        WriteEnvelopeStart(writer, "Healthy");
        writer.WriteStartArray("components");
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    internal static async Task WriteHealthAsync(
        HttpContext context,
        HealthReport report,
        IReadOnlySet<string> publishedComponentNames)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(publishedComponentNames);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";

        await using var writer = CreateWriter(context);
        WriteEnvelopeStart(writer, report.Status.ToString());
        writer.WriteStartArray("components");
        foreach (var entry in report.Entries
                     .Where(entry => publishedComponentNames.Contains(entry.Key))
                     .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", entry.Key);
            writer.WriteString("status", entry.Value.Status.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    internal static async Task WriteReleaseAsync(HttpContext context)
    {
        Cp6ReleaseIdentity? identity = null;
        var valid = false;
        try
        {
            var accessor = context.RequestServices.GetRequiredService<ICp6ReleaseIdentityAccessor>();
            var registration = context.RequestServices.GetRequiredService<Cp6ObservabilityRegistration>();
            identity = accessor.Current;
            identity.Validate();
            valid = identity == registration.Profile.ReleaseIdentity;
        }
        catch (Exception)
        {
            valid = false;
        }

        PrepareResponse(
            context,
            valid ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        await using var writer = CreateWriter(context);
        WriteEnvelopeStart(writer, valid ? "Healthy" : "Unhealthy");
        if (valid && identity is not null)
        {
            writer.WriteStartObject("release");
            writer.WriteString("service", identity.Service);
            writer.WriteString("version", identity.Version);
            WriteWhenPresent(writer, "gitSha", identity.GitSha);
            WriteWhenPresent(writer, "artifactDigest", identity.ArtifactDigest);
            WriteWhenPresent(writer, "contractBundleDigest", identity.ContractBundleDigest);
            writer.WriteBoolean("candidate", identity.Candidate);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    private static Utf8JsonWriter CreateWriter(HttpContext context) =>
        new(context.Response.BodyWriter, new JsonWriterOptions { Indented = false });

    private static void PrepareResponse(HttpContext context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
    }

    private static void WriteEnvelopeStart(Utf8JsonWriter writer, string status)
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", SchemaVersion);
        writer.WriteString("status", status);
        writer.WriteString("observedAtUtc", DateTimeOffset.UtcNow.UtcDateTime);
    }

    private static void WriteWhenPresent(Utf8JsonWriter writer, string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(name, value);
        }
    }
}
