using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using CP6.Platform.Contracts;
using CP6.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace CP6.Platform.AspNetCoreTests;

internal sealed class TwoServiceObservabilityFixture : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebApplication serviceA;
    private readonly WebApplication serviceB;
    private readonly RequestCounter serviceBRequests;
    private readonly ThrowingActivityExporter? throwingExporter;

    private TwoServiceObservabilityFixture(
        WebApplication serviceA,
        WebApplication serviceB,
        Uri serviceAAddress,
        Cp6TelemetryRecorder recorder,
        RequestCounter serviceBRequests,
        ThrowingActivityExporter? throwingExporter)
    {
        this.serviceA = serviceA;
        this.serviceB = serviceB;
        this.serviceBRequests = serviceBRequests;
        this.throwingExporter = throwingExporter;
        ServiceAAddress = serviceAAddress;
        Recorder = recorder;
        Client = new HttpClient { BaseAddress = serviceAAddress };
        ServiceAResource = Resource(serviceA.Services);
        ServiceBResource = Resource(serviceB.Services);
    }

    public HttpClient Client { get; }

    public Cp6TelemetryRecorder Recorder { get; }

    public Uri ServiceAAddress { get; }

    public int ServiceBRequestCount => serviceBRequests.Count;

    public int ThrowingExporterAttempts => throwingExporter?.AttemptCount ?? 0;

    public IReadOnlyDictionary<string, object> ServiceAResource { get; }

    public IReadOnlyDictionary<string, object> ServiceBResource { get; }

    public static async Task<TwoServiceObservabilityFixture> StartAsync(
        TwoServiceObservabilityOptions? options = null)
    {
        options ??= new TwoServiceObservabilityOptions();
        var recorder = new Cp6TelemetryRecorder(
            ["Microsoft.AspNetCore", "System.Net.Http", .. Cp6TelemetrySources.All],
            Cp6TelemetryMeters.All);
        WebApplication? serviceB = null;
        WebApplication? serviceA = null;
        try
        {
            var requests = new RequestCounter();
            serviceB = await StartServiceBAsync(requests);
            var exporter = options.UseThrowingExporter ? new ThrowingActivityExporter() : null;
            serviceA = await StartServiceAAsync(Address(serviceB), options, exporter);
            return new TwoServiceObservabilityFixture(
                serviceA,
                serviceB,
                Address(serviceA),
                recorder,
                requests,
                exporter);
        }
        catch
        {
            if (serviceA is not null)
            {
                await serviceA.DisposeAsync();
            }

            if (serviceB is not null)
            {
                await serviceB.DisposeAsync();
            }

            recorder.Dispose();
            throw;
        }
    }

    public async Task<RawHttpResponse> SendRawAsync(
        HttpMethod method,
        string path,
        params (string Name, string Value)[] headers)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(ServiceAAddress.Host, ServiceAAddress.Port);
        await using var stream = tcp.GetStream();
        var request = new StringBuilder()
            .Append(method.Method).Append(' ').Append(path).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(ServiceAAddress.Host).Append(':').Append(ServiceAAddress.Port).Append("\r\n")
            .Append("Connection: close\r\n");
        foreach (var (name, value) in headers)
        {
            request.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        if (method != HttpMethod.Get && method != HttpMethod.Head)
        {
            request.Append("Content-Length: 0\r\n");
        }

        request.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(request.ToString());
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var raw = await reader.ReadToEndAsync();
        return ParseResponse(raw);
    }

    public Task WaitForFaultAttemptsAsync(int expected) => WaitUntilAsync(
        () => serviceA.Services.GetService<Cp6HttpFaultScript>()?.AttemptCount >= expected,
        "fault script attempt");

    public Task WaitForExporterAttemptAsync() => WaitUntilAsync(
        () => ThrowingExporterAttempts > 0,
        "throwing exporter attempt");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await serviceA.StopAsync();
        await serviceA.DisposeAsync();
        await serviceB.StopAsync();
        await serviceB.DisposeAsync();

        Recorder.Dispose();
    }

    private static async Task<WebApplication> StartServiceBAsync(RequestCounter requests)
    {
        var builder = Builder("cp6-test-service-b");
        builder.Services.AddSingleton(requests);
        var application = builder.Build();
        application.UseMiddleware<Cp6CorrelationMiddleware>();
        application.MapMethods("/ok", ["GET", "POST"], (HttpContext context, RequestCounter counter) =>
        {
            counter.Increment();
            return Results.Json(new ProxyResponse(
                true,
                context.TraceIdentifier,
                string.Empty,
                context.Request.Headers.ContainsKey("traceparent"),
                context.Request.Headers.ContainsKey("baggage")));
        });
        await application.StartAsync();
        return application;
    }

    private static async Task<WebApplication> StartServiceAAsync(
        Uri serviceBAddress,
        TwoServiceObservabilityOptions options,
        ThrowingActivityExporter? exporter)
    {
        var builder = Builder("cp6-test-service-a");
        if (options.TimeProvider is not null)
        {
            builder.Services.AddSingleton(options.TimeProvider);
        }

        var clientBuilder = builder.Services
            .AddHttpClient("service-b", client => client.BaseAddress = serviceBAddress)
            .AddCp6HttpResilience(new Cp6HttpResilienceProfile(
                "service-b",
                options.OperationKind,
                options.OperationKind == Cp6HttpOperationKind.NonIdempotent ? 0 : options.RetryAttempts,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10),
                options.CircuitMinimumThroughput,
                options.CircuitBreakDuration));
        if (options.FaultScript is not null)
        {
            builder.Services.AddCp6HttpFaultInjection(builder.Environment, options.FaultScript);
            clientBuilder.AddHttpMessageHandler<Cp6HttpFaultHandler>();
        }

        if (exporter is not null)
        {
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddProcessor(
                new BatchActivityExportProcessor(
                    new NonThrowingActivityExporter(exporter),
                    maxQueueSize: 64,
                    scheduledDelayMilliseconds: 10,
                    exporterTimeoutMilliseconds: 100,
                    maxExportBatchSize: 16)));
        }

        var application = builder.Build();
        application.UseMiddleware<Cp6CorrelationMiddleware>();
        application.MapGet("/proxy/read", (HttpContext context, IHttpClientFactory clients) =>
            ForwardAsync(context, clients, HttpMethod.Get));
        application.MapPost("/proxy/write", (HttpContext context, IHttpClientFactory clients) =>
            ForwardAsync(context, clients, HttpMethod.Post));
        await application.StartAsync();
        return application;
    }

    private static WebApplicationBuilder Builder(string serviceName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test",
            ApplicationName = typeof(TwoServiceObservabilityFixture).Assembly.FullName
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Services.AddCp6Observability(Profile(serviceName));
        return builder;
    }

    private static async Task<IResult> ForwardAsync(
        HttpContext context,
        IHttpClientFactory clients,
        HttpMethod method)
    {
        using var request = new HttpRequestMessage(method, "/ok");
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeys))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKeys.ToArray());
        }

        try
        {
            using var response = await clients.CreateClient("service-b").SendAsync(request, context.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                return Results.Json(
                    new ProxyResponse(false, context.TraceIdentifier, "downstream_status"),
                    statusCode: (int)response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<ProxyResponse>(JsonOptions, context.RequestAborted);
            return Results.Json(body, statusCode: (int)response.StatusCode);
        }
        catch (Cp6HttpResilienceException exception)
        {
            return Results.Json(
                new ProxyResponse(false, context.TraceIdentifier, exception.Category.ToString()),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Results.Json(
                new ProxyResponse(false, context.TraceIdentifier, "downstream_unavailable"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
    }

    private static Cp6ObservabilityProfile Profile(string serviceName)
    {
        const string version = "0.8.0-alpha.1";
        var identity = new Cp6ReleaseIdentity(
            serviceName,
            version,
            new string('a', 40),
            "sha256:" + new string('b', 64),
            "sha256:" + new string('c', 64),
            Cp6ReleaseMode.Candidate);
        return new Cp6ObservabilityProfile(serviceName, version, "test", "us-east", identity);
    }

    private static Uri Address(WebApplication application)
    {
        var addresses = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        return new Uri(Assert.Single(addresses ?? []));
    }

    private static IReadOnlyDictionary<string, object> Resource(IServiceProvider services) =>
        services.GetRequiredService<TracerProvider>()
            .GetResource()
            .Attributes
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

    private static RawHttpResponse ParseResponse(string raw)
    {
        var separator = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new InvalidOperationException("Raw HTTP response did not contain a header terminator.");
        }

        var headerBlock = raw[..separator];
        var lines = headerBlock.Split("\r\n", StringSplitOptions.None);
        var statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], CultureInfo.InvariantCulture, out var statusCode))
        {
            throw new InvalidOperationException("Raw HTTP response contained an invalid status line.");
        }

        var body = raw[(separator + 4)..];
        if (lines.Any(line => string.Equals(line, "Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase)))
        {
            body = DecodeChunked(body);
        }

        return new RawHttpResponse((HttpStatusCode)statusCode, body);
    }

    private static string DecodeChunked(string body)
    {
        var decoded = new StringBuilder();
        var position = 0;
        while (position < body.Length)
        {
            var lineEnd = body.IndexOf("\r\n", position, StringComparison.Ordinal);
            if (lineEnd < 0 ||
                !int.TryParse(body[position..lineEnd], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length))
            {
                throw new InvalidOperationException("Raw HTTP response contained invalid chunk framing.");
            }

            position = lineEnd + 2;
            if (length == 0)
            {
                break;
            }

            decoded.Append(body, position, length);
            position += length + 2;
        }

        return decoded.ToString();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class RequestCounter
    {
        private int count;

        internal int Count => Volatile.Read(ref count);

        internal void Increment() => Interlocked.Increment(ref count);
    }

    private sealed class ThrowingActivityExporter : BaseExporter<Activity>
    {
        private int attemptCount;

        internal int AttemptCount => Volatile.Read(ref attemptCount);

        public override ExportResult Export(in Batch<Activity> batch)
        {
            Interlocked.Increment(ref attemptCount);
            throw new InvalidOperationException("Synthetic exporter failure.");
        }
    }

    private sealed class NonThrowingActivityExporter(BaseExporter<Activity> inner) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            try
            {
                return inner.Export(batch);
            }
            catch (Exception)
            {
                return ExportResult.Failure;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed record TwoServiceObservabilityOptions
{
    internal Cp6HttpOperationKind OperationKind { get; init; } = Cp6HttpOperationKind.IdempotentRead;

    internal Cp6HttpFaultScript? FaultScript { get; init; }

    internal TimeProvider? TimeProvider { get; init; }

    internal int RetryAttempts { get; init; } = 2;

    internal int CircuitMinimumThroughput { get; init; } = 10;

    internal TimeSpan CircuitBreakDuration { get; init; } = TimeSpan.FromSeconds(30);

    internal bool UseThrowingExporter { get; init; }

}

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private DateTimeOffset utcNow = DateTimeOffset.UnixEpoch;
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return timestamp;
        }
    }

    internal void Advance(TimeSpan duration)
    {
        lock (gate)
        {
            utcNow = utcNow.Add(duration);
            timestamp += duration.Ticks;
        }
    }
}

internal sealed record RawHttpResponse(HttpStatusCode StatusCode, string Body);

internal sealed record ProxyResponse(
    bool Success,
    string CorrelationId,
    string ErrorCode,
    bool HasTraceParentHeader = false,
    bool HasBaggageHeader = false);
