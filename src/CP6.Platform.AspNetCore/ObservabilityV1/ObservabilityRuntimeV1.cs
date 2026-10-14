using System.Diagnostics;
using System.Diagnostics.Metrics;
using CP6.Platform.Abstractions;
using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CP6.Platform.AspNetCore.ObservabilityV1;

public static class ObservabilityRegistrationV1
{
    public static IServiceCollection AddCp6ObservabilityV1(this IServiceCollection services, TelemetryPolicyV1 policy, LocalTelemetrySinkV1 sink, TelemetryLimitsV1? limits = null)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(sink);
        if (services.Any(s => s.ServiceType == typeof(ObservabilityRuntimeV1))) throw new InvalidOperationException("Observability V1 already registered; registration drift is forbidden.");
        services.AddSingleton(policy); services.AddSingleton(sink); services.AddSingleton(limits ?? new TelemetryLimitsV1());
        services.AddSingleton<ObservabilityRuntimeV1>();
        services.AddSingleton<ILoggerProvider>(sp => new PrivacyLoggerProviderV1(sp.GetRequiredService<ObservabilityRuntimeV1>()));
        services.AddTransient<IStartupFilter, ObservationStartupFilterV1>();
        services.AddHostedService<ObservationHostedServiceV1>();
        return services;
    }
}

/// <summary>Per-host SDK adapter. Privacy applies only to this registration's local export path.</summary>
public sealed class ObservabilityRuntimeV1 : IAsyncDisposable
{
    internal static readonly AsyncLocal<ObservabilityRuntimeV1?> Current = new();
    private const string OwnerKey = "cp6.observation.v1.owner";
    private readonly TelemetryPolicyV1 policy;
    private readonly TelemetryLimitsV1 limits;
    private readonly SafeExportQueueV1 queue;
    private readonly PropagationLeaseV1 propagation;
    private readonly TracerProvider traces;
    private readonly MeterProvider metrics;
    private readonly MeterListener bridge;
    private readonly Meter safeMeter;
    private readonly Dictionary<string, Counter<double>> counters = [];
    private readonly Dictionary<string, Histogram<double>> histograms = [];
    private readonly HashSet<string> series = new(StringComparer.Ordinal);
    private long measurementWindow = Environment.TickCount64;
    private int measurementCount;
    private readonly ILoggerFactory safeLogs;
    private readonly ILogger safeLogger;
    private int disposed;

    public ObservabilityRuntimeV1(TelemetryPolicyV1 policy, LocalTelemetrySinkV1 sink, TelemetryLimitsV1 limits)
    {
        this.policy = policy; this.limits = limits;
        propagation = new PropagationLeaseV1(); queue = new SafeExportQueueV1(sink, limits);
        var meterName = "CP6.ObservabilityV1.Safe." + Guid.NewGuid().ToString("N");
        safeMeter = new Meter(meterName);
        var resource = ResourceBuilder.CreateEmpty().AddAttributes(policy.Resources.Select(x => new KeyValuePair<string, object>(x.Key, x.Value)));
        metrics = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource).AddMeter(meterName)
            .AddView(_ => new MetricStreamConfiguration { CardinalityLimit = limits.MaximumSeries })
            .AddReader(new BaseExportingMetricReader(new SafeMetricExporter(this)) { TemporalityPreference = MetricReaderTemporalityPreference.Delta }).Build();
        foreach (var definition in MetricDefinitionV1.Technical)
        {
            if (definition.Kind == MetricKindV1.Counter) counters.Add(definition.Name, safeMeter.CreateCounter<double>(definition.Name, definition.Unit));
            else histograms.Add(definition.Name, safeMeter.CreateHistogram<double>(definition.Name, definition.Unit));
        }
        bridge = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (MetricDefinitionV1.Technical.Any(d => d.Source == instrument.Meter.Name && d.Name == instrument.Name)) listener.EnableMeasurementEvents(instrument);
            }
        };
        bridge.SetMeasurementEventCallback<long>((i, value, tags, _) => Bridge(i.Name, value, tags));
        bridge.SetMeasurementEventCallback<int>((i, value, tags, _) => Bridge(i.Name, value, tags));
        bridge.SetMeasurementEventCallback<double>((i, value, tags, _) => Bridge(i.Name, value, tags));
        bridge.Start();
        traces = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource)
            .AddSource(Cp6TelemetrySources.All.ToArray())
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = false;
            })
            .AddHttpClientInstrumentation(options => options.RecordException = false)
            .AddProcessor(new SafeTraceProcessor(this)).Build();
        safeLogs = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = false; options.IncludeScopes = false; options.ParseStateValues = false;
            options.SetResourceBuilder(resource); options.AddProcessor(new SafeLogProcessor(this));
        }));
        safeLogger = safeLogs.CreateLogger("cp6.safe");
    }

    public long DroppedCount => queue.Dropped;
    public bool PropagationIntact => propagation.Intact;
    // No denominator, sampling completeness or independent qualification exists in this local adapter.
    public TelemetryCompletenessV1 Completeness => TelemetryCompletenessV1.Incomplete;
    public bool ExternalExportEnabled => false;

    public IDisposable BeginScope()
    {
        var previous = Current.Value; Current.Value = this;
        return new Scope(() => Current.Value = previous);
    }

    internal void BindInboundActivity() => Activity.Current?.SetCustomProperty(OwnerKey, this);

    private void Bridge(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> raw)
    {
        try
        {
            if (!ReferenceEquals(Current.Value, this)) return;
            if (raw.Length > 32 || !double.IsFinite(value) || value < 0 || value > 3_600_000) { queue.Drop(); return; }
            var safe = policy.Sanitize(raw.ToArray());
            RecordMeasurement(name, value, safe);
        }
        catch (Exception error) when (IsOrdinaryTelemetryFailure(error)) { queue.Drop(); }
    }

    private void RecordMeasurement(string name, double value, IReadOnlyDictionary<string, string> attributes)
    {
        if (!PropagationIntact || Volatile.Read(ref disposed) != 0) { queue.Drop(); return; }
        var key = name + "|" + string.Join('|', attributes.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value));
        lock (series)
        {
            var now = Environment.TickCount64;
            if (now - measurementWindow >= 1000) { measurementWindow = now; measurementCount = 0; }
            if (++measurementCount > limits.RecordsPerSecond) { queue.Drop(); return; }
            if (!series.Contains(key) && series.Count >= limits.MaximumSeries) { queue.Drop(); return; }
            series.Add(key);
        }
        var tags = new TagList(); foreach (var attribute in attributes) tags.Add(attribute.Key, attribute.Value);
        if (counters.TryGetValue(name, out var counter)) counter.Add(value, tags);
        else if (histograms.TryGetValue(name, out var histogram)) histogram.Record(value, tags);
    }

    internal void Log(LogLevel level)
    {
        if (Volatile.Read(ref disposed) != 0) { queue.Drop(); return; }
        if (!Enum.IsDefined(level)) { queue.Drop(); return; }
        // Only this finite severity and constant message enter the log SDK. Never invoke the original formatter.
        // Cache the SDK logger before any shutdown can occur. Even if disposal races the preceding
        // state check, an ordinary SDK shutdown error is a dropped observation, not a caller error.
        try { safeLogger.Log(level, default, "event", null, static (state, _) => state); }
        catch (Exception error) when (IsOrdinaryTelemetryFailure(error)) { queue.Drop(); }
    }

    private static bool IsOrdinaryTelemetryFailure(Exception error) => error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private void Export(TelemetrySignalV1 signal, string name, double value, long count, IEnumerable<KeyValuePair<string, object?>> attributes)
    {
        if (!PropagationIntact || !double.IsFinite(value) || value < 0) { queue.Drop(); return; }
        queue.TryEnqueue(new SafeTelemetryRecordV1(signal, name, value, count, policy.Sanitize(attributes), policy));
    }

    public async Task<bool> FlushAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (disposed != 0) return false;
        metrics.ForceFlush((int)Math.Max(1, timeout.TotalMilliseconds));
        traces.ForceFlush((int)Math.Max(1, timeout.TotalMilliseconds));
        return await queue.FlushAsync(timeout);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        bridge.Dispose(); traces.Dispose(); metrics.Dispose(); safeLogs.Dispose(); safeMeter.Dispose();
        await queue.DisposeAsync(); propagation.Dispose();
    }

    private sealed class SafeTraceProcessor(ObservabilityRuntimeV1 owner) : BaseProcessor<Activity>
    {
        public override void OnStart(Activity activity)
        {
            if (ReferenceEquals(Current.Value, owner)) activity.SetCustomProperty(OwnerKey, owner);
        }
        public override void OnEnd(Activity activity)
        {
            if (!ReferenceEquals(activity.GetCustomProperty(OwnerKey), owner)) return;
            var safe = owner.policy.Sanitize(activity.TagObjects);
            var tags = safe.Select(x => new KeyValuePair<string, object?>(x.Key, x.Value)).ToList();
            if (activity.GetTagItem("http.response.status_code") is int status && status is >= 100 and < 600)
                tags.Add(new("http.response.status_class", $"{status / 100}xx"));
            var name = activity.Kind switch { ActivityKind.Server => "http.server", ActivityKind.Client => "http.client", _ => "cp6.operation" };
            var duration = Math.Clamp(activity.Duration.TotalMilliseconds, 0, 3_600_000);
            owner.Export(TelemetrySignalV1.Trace, name, duration, 1, tags);
            if (activity.Kind is ActivityKind.Server or ActivityKind.Client) owner.RecordMeasurement("cp6.http.duration", duration, owner.policy.Sanitize(tags));
        }
    }
    private sealed class SafeLogProcessor(ObservabilityRuntimeV1 owner) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) => owner.Export(TelemetrySignalV1.Log, "log.event", 0, 1,
            [new("log.severity", data.LogLevel.ToString())]);
    }
    private sealed class SafeMetricExporter(ObservabilityRuntimeV1 owner) : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                if (!MetricDefinitionV1.Technical.Any(d => d.Name == metric.Name)) continue;
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var tags = new List<KeyValuePair<string, object?>>();
                    foreach (var pair in point.Tags) { if (tags.Count == 32) break; tags.Add(pair); }
                    var histogram = metric.MetricType == MetricType.Histogram;
                    owner.Export(TelemetrySignalV1.Metric, metric.Name, histogram ? point.GetHistogramSum() : point.GetSumDouble(), histogram ? point.GetHistogramCount() : 1, tags);
                }
            }
            return ExportResult.Success;
        }
    }
    private sealed class Scope(Action end) : IDisposable { private Action? action = end; public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke(); }
}

internal sealed class PrivacyLoggerProviderV1(ObservabilityRuntimeV1 runtime) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new PrivacyLogger(runtime);
    public void Dispose() { }
    private sealed class PrivacyLogger(ObservabilityRuntimeV1 owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Log(logLevel);
    }
}
internal sealed class ObservationStartupFilterV1(ObservabilityRuntimeV1 runtime) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    {
        application.Use(async (_, nextMiddleware) => { using var scope = runtime.BeginScope(); runtime.BindInboundActivity(); await nextMiddleware(); }); next(application);
    };
}
internal sealed class ObservationHostedServiceV1(ObservabilityRuntimeV1 runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) { _ = runtime.PropagationIntact; return Task.CompletedTask; }
    public async Task StopAsync(CancellationToken cancellationToken) => await runtime.DisposeAsync();
}
