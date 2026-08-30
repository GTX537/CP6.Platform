using System.Net;
using CP6.Platform.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests;

public sealed class HttpResilienceTests
{
    [Theory]
    [InlineData(-1, 100, 250, 1, 2, 1)]
    [InlineData(6, 100, 250, 1, 2, 1)]
    [InlineData(1, 99, 250, 1, 2, 1)]
    [InlineData(1, 30001, 31000, 1, 2, 1)]
    [InlineData(1, 100, 249, 1, 2, 1)]
    [InlineData(1, 100, 120001, 1, 2, 1)]
    [InlineData(1, 100, 250, 0, 2, 1)]
    [InlineData(1, 100, 250, 121, 2, 1)]
    [InlineData(1, 100, 250, 1, 1, 1)]
    [InlineData(1, 100, 250, 1, 1001, 1)]
    [InlineData(1, 100, 250, 1, 2, 0)]
    [InlineData(1, 100, 250, 1, 2, 301)]
    public void Profile_RejectsValuesOutsideApprovedBounds(
        int retries,
        int attemptMilliseconds,
        int totalMilliseconds,
        int samplingSeconds,
        int minimumThroughput,
        int breakSeconds)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Cp6HttpResilienceProfile(
            "crm",
            Cp6HttpOperationKind.IdempotentRead,
            retries,
            TimeSpan.FromMilliseconds(attemptMilliseconds),
            TimeSpan.FromMilliseconds(totalMilliseconds),
            TimeSpan.FromSeconds(samplingSeconds),
            minimumThroughput,
            TimeSpan.FromSeconds(breakSeconds)));
    }

    [Fact]
    public void Profile_RejectsUnknownOperation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6HttpResilienceProfile(
            "crm",
            (Cp6HttpOperationKind)999));
    }

    [Fact]
    public void NonIdempotentProfile_ForcesRetryAttemptsToZero()
    {
        var profile = Profile(Cp6HttpOperationKind.NonIdempotent, retryAttempts: 5);

        Assert.Equal(0, profile.RetryAttempts);
    }

    [Fact]
    public void Registration_IsIdempotentAndRejectsDrift()
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("crm");
        var profile = Profile(Cp6HttpOperationKind.IdempotentRead);

        builder.AddCp6HttpResilience(profile);
        var count = services.Count;
        builder.AddCp6HttpResilience(profile);

        Assert.Equal(count, services.Count);
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddCp6HttpResilience(Profile(Cp6HttpOperationKind.NonIdempotent)));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task IdempotentRead_AllowsOnlyApprovedReadMethods(string method)
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(Profile(Cp6HttpOperationKind.IdempotentRead), transport);

        using var response = await fixture.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/resource"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task IdempotentRead_RejectsWriteMethodBeforeNetwork()
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(Profile(Cp6HttpOperationKind.IdempotentRead), transport);

        var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.PostAsync("/resource", new StringContent("{}")));

        Assert.Equal(Cp6HttpFailureCategory.OperationNotAllowed, exception.Category);
        Assert.Equal(0, transport.Attempts);
    }

    [Theory]
    [InlineData(Cp6HttpOperationKind.IdempotentWrite)]
    [InlineData(Cp6HttpOperationKind.NonIdempotent)]
    public async Task WriteKinds_RejectReadMethodBeforeNetwork(Cp6HttpOperationKind operationKind)
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(Profile(operationKind), transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/resource");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "stable-key");

        var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.SendAsync(request));

        Assert.Equal(Cp6HttpFailureCategory.OperationNotAllowed, exception.Category);
        Assert.Equal(0, transport.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("key/with/slash")]
    public async Task IdempotentWrite_RequiresOneStableKey(string? key)
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(Profile(Cp6HttpOperationKind.IdempotentWrite), transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = new StringContent("{}")
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.SendAsync(request));

        Assert.Equal(Cp6HttpFailureCategory.IdempotencyRequired, exception.Category);
        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task IdempotentWrite_RejectsDuplicateKeyBeforeNetwork()
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(Profile(Cp6HttpOperationKind.IdempotentWrite), transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", new[] { "stable-key", "other-key" });

        var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.SendAsync(request));

        Assert.Equal(Cp6HttpFailureCategory.IdempotencyRequired, exception.Category);
        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task IdempotentWrite_WithStableKey_RetriesApprovedFailure()
    {
        var transport = new ScriptedHandler(attempt => new HttpResponseMessage(
            attempt < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.IdempotentWrite, retryAttempts: 2),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "order.create:123");

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, transport.Attempts);
        Assert.Equal(1, transport.MaximumConcurrency);
    }

    [Fact]
    public async Task NonIdempotent_NeverRetries()
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.NonIdempotent, retryAttempts: 5),
            transport);

        using var response = await fixture.Client.PostAsync("/orders", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, transport.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ApprovedTransientStatuses_AreRetried(HttpStatusCode statusCode)
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(statusCode));
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.IdempotentRead, retryAttempts: 2),
            transport);

        using var response = await fixture.Client.GetAsync("/resource");

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(3, transport.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported)]
    public async Task UnapprovedStatuses_AreNotRetried(HttpStatusCode statusCode)
    {
        var transport = new ScriptedHandler(_ => new HttpResponseMessage(statusCode));
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.IdempotentRead, retryAttempts: 2),
            transport);

        using var response = await fixture.Client.GetAsync("/resource");

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task HttpRequestException_IsRetriedExactly()
    {
        var transport = new ScriptedHandler(_ => throw new HttpRequestException("dependency unavailable"));
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.IdempotentRead, retryAttempts: 2),
            transport);

        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Client.GetAsync("/resource"));

        Assert.Equal(3, transport.Attempts);
    }

    [Fact]
    public async Task UnapprovedException_IsNotRetried()
    {
        var transport = new ScriptedHandler(_ => throw new InvalidOperationException("not transient"));
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.IdempotentRead, retryAttempts: 5),
            transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.GetAsync("/resource"));

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task AttemptTimeout_HasStableCategory()
    {
        var transport = new ScriptedHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var fixture = ClientFixture.Create(
            Profile(
                Cp6HttpOperationKind.IdempotentRead,
                retryAttempts: 0,
                attemptTimeout: TimeSpan.FromMilliseconds(100),
                totalTimeout: TimeSpan.FromSeconds(2)),
            transport);

        var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.GetAsync("/resource"));

        Assert.Equal(Cp6HttpFailureCategory.AttemptTimeout, exception.Category);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task TotalTimeout_HasStableCategory()
    {
        var transport = new ScriptedHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var fixture = ClientFixture.Create(
            Profile(
                Cp6HttpOperationKind.IdempotentRead,
                retryAttempts: 0,
                attemptTimeout: TimeSpan.FromSeconds(30),
                totalTimeout: TimeSpan.FromMilliseconds(250)),
            transport);

        var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.GetAsync("/resource"));

        Assert.Equal(Cp6HttpFailureCategory.TotalTimeout, exception.Category);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutRetryOrTranslation()
    {
        var transport = new ScriptedHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var fixture = ClientFixture.Create(
            Profile(Cp6HttpOperationKind.IdempotentRead, retryAttempts: 5),
            transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.GetAsync("/resource", cancellation.Token));

        Assert.InRange(transport.Attempts, 0, 1);
    }

    [Fact]
    public async Task Circuit_Opens_HalfOpens_AndRecoversWithManualTime()
    {
        var time = new ManualTimeProvider();
        var transport = new ScriptedHandler(attempt => new HttpResponseMessage(
            attempt <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using var fixture = ClientFixture.Create(
            Profile(
                Cp6HttpOperationKind.NonIdempotent,
                circuitSampling: TimeSpan.FromSeconds(1),
                minimumThroughput: 2,
                breakDuration: TimeSpan.FromSeconds(1)),
            transport,
            services => services.AddSingleton<TimeProvider>(time));

        using var first = await fixture.Client.PostAsync("/orders", new StringContent("{}"));
        using var second = await fixture.Client.PostAsync("/orders", new StringContent("{}"));
        var open = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
            () => fixture.Client.PostAsync("/orders", new StringContent("{}")));

        Assert.Equal(Cp6HttpFailureCategory.CircuitOpen, open.Category);
        Assert.Equal(2, transport.Attempts);

        time.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        using var recovery = await fixture.Client.PostAsync("/orders", new StringContent("{}"));
        using var closed = await fixture.Client.PostAsync("/orders", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(4, transport.Attempts);
    }

    private static Cp6HttpResilienceProfile Profile(
        Cp6HttpOperationKind operationKind,
        int retryAttempts = 2,
        TimeSpan? attemptTimeout = null,
        TimeSpan? totalTimeout = null,
        TimeSpan? circuitSampling = null,
        int minimumThroughput = 10,
        TimeSpan? breakDuration = null) => new(
            "crm",
            operationKind,
            retryAttempts,
            attemptTimeout ?? TimeSpan.FromSeconds(2),
            totalTimeout ?? TimeSpan.FromSeconds(10),
            circuitSampling ?? TimeSpan.FromSeconds(10),
            minimumThroughput,
            breakDuration ?? TimeSpan.FromSeconds(30));

    private sealed class ClientFixture(ServiceProvider provider, HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public static ClientFixture Create(
            Cp6HttpResilienceProfile profile,
            HttpMessageHandler transport,
            Action<IServiceCollection>? configureServices = null)
        {
            var services = new ServiceCollection();
            configureServices?.Invoke(services);
            services.AddHttpClient(profile.ClientName, client => client.BaseAddress = new Uri("https://example.test"))
                .ConfigurePrimaryHttpMessageHandler(() => transport)
                .AddCp6HttpResilience(profile);
            var provider = services.BuildServiceProvider();
            return new ClientFixture(
                provider,
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(profile.ClientName));
        }

        public void Dispose()
        {
            Client.Dispose();
            provider.Dispose();
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<int, CancellationToken, Task<HttpResponseMessage>> send;
        private int attempts;
        private int concurrency;
        private int maximumConcurrency;

        public ScriptedHandler(Func<int, HttpResponseMessage> send)
            : this((attempt, _) => Task.FromResult(send(attempt)))
        {
        }

        public ScriptedHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        public int Attempts => Volatile.Read(ref attempts);

        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref attempts);
            var current = Interlocked.Increment(ref concurrency);
            UpdateMaximum(current);
            try
            {
                return await send(attempt, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref concurrency);
            }
        }

        private void UpdateMaximum(int current)
        {
            while (true)
            {
                var observed = Volatile.Read(ref maximumConcurrency);
                if (current <= observed ||
                    Interlocked.CompareExchange(ref maximumConcurrency, current, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
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

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (gate)
            {
                utcNow += duration;
                timestamp += duration.Ticks;
                due = timers.Where(timer => timer.IsDue(timestamp)).ToArray();
            }

            foreach (var timer in due)
            {
                timer.Fire(timestamp);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider owner;
            private readonly TimerCallback callback;
            private readonly object? state;
            private TimeSpan period;
            private long dueTimestamp;
            private bool disposed;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                this.owner = owner;
                this.callback = callback;
                this.state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    this.period = period;
                    dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : owner.timestamp + dueTime.Ticks;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(long now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueTimestamp <= now;
                }
            }

            public void Fire(long now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueTimestamp > now)
                    {
                        return;
                    }

                    dueTimestamp = period == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : now + period.Ticks;
                }

                callback(state);
            }
        }
    }
}
