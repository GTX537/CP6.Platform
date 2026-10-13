using System.Net;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using Polly.Timeout;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class HandlerTests
{
    [Theory]
    [InlineData(408, 3)] [InlineData(429, 3)] [InlineData(500, 3)] [InlineData(502, 3)] [InlineData(503, 3)] [InlineData(504, 3)]
    [InlineData(401, 1)] [InlineData(403, 1)] [InlineData(409, 1)] [InlineData(422, 1)] [InlineData(400, 1)] [InlineData(200, 1)]
    public async Task ApprovedStatusSetHasBoundedAttempts_AndNoBusinessRejectionsRetry(int status, int expected)
    {
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var client = Client(Profile(), transport);
        using var response = await client.GetAsync("https://owner.example/read"); Assert.Equal(expected, transport.Attempts);
    }
    [Theory]
    [InlineData("GET")] [InlineData("HEAD")] [InlineData("OPTIONS")]
    public async Task DisabledReadIsOneAttempt(string method)
    {
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(Profile(mode: Cp6HttpRetryMode.Disabled), transport);
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "https://owner.example/read"));
        Assert.Equal(1, transport.Attempts);
    }
    [Theory]
    [InlineData("POST")] [InlineData("PUT")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task NoProofWriteAndNonIdempotentAreNeverRetried(string method)
    {
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(Profile(Cp6HttpOperationKind.IdempotentWrite, Cp6HttpRetryMode.Disabled), transport);
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://owner.example/write");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "forged-key");
        using var response = await client.SendAsync(request); Assert.Equal(1, transport.Attempts);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome));
        Assert.Equal(Cp6HttpOperationState.Unknown, outcome.State);
    }
    [Theory]
    [InlineData(Cp6HttpOperationKind.IdempotentRead, "POST")]
    [InlineData(Cp6HttpOperationKind.IdempotentWrite, "GET")]
    [InlineData(Cp6HttpOperationKind.NonIdempotent, "HEAD")]
    public async Task MethodClassMismatchStopsBeforeNetwork(Cp6HttpOperationKind kind, string method)
    {
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = Client(Profile(kind, Cp6HttpRetryMode.Disabled), transport);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "https://owner.example/x")));
        Assert.Equal(Cp6HttpV2Failure.OperationNotAllowed, error.Outcome.Failure); Assert.Equal(0, transport.Attempts);
    }
    [Fact]
    public async Task HttpRequestExceptionRetries_UnknownPollyTimeoutDoesNot()
    {
        var transport = new Scripted((_, _) => throw new HttpRequestException("fixture"));
        using var client = Client(Profile(), transport);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync("https://owner.example/read"));
        Assert.Equal(Cp6HttpV2Failure.Transport, error.Outcome.Failure); Assert.Equal(3, transport.Attempts);
        var unknown = new Scripted((_, _) => throw new TimeoutRejectedException());
        using var other = Client(Profile(), unknown);
        var timeout = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => other.GetAsync("https://owner.example/read"));
        Assert.Equal(Cp6HttpV2Failure.UnknownTimeoutSource, timeout.Outcome.Failure); Assert.Equal(1, unknown.Attempts);
    }
    [Fact]
    public async Task UnrelatedOperationCancellationIsUnknownSource_NotAttemptTimeout()
    {
        var transport = new Scripted((_, _) => throw new OperationCanceledException(new CancellationToken(true)));
        using var client = Client(Profile(), transport);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync("https://owner.example/read"));
        Assert.Equal(Cp6HttpV2Failure.UnknownTimeoutSource, error.Outcome.Failure); Assert.Equal(1, transport.Attempts);
    }
    [Theory]
    [InlineData(100, 300, Cp6HttpV2Failure.AttemptTimeout)]
    [InlineData(250, 250, Cp6HttpV2Failure.TotalTimeout)]
    public async Task ExplicitDeadlineSource_DistinguishesEqualDurations(int attemptMs, int totalMs, Cp6HttpV2Failure expected)
    {
        var clock = new TestClock(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Scripted(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); });
        var profile = new Cp6HttpV2Profile("test", Cp6HttpOperationKind.IdempotentRead, attemptTimeout: TimeSpan.FromMilliseconds(attemptMs), totalTimeout: TimeSpan.FromMilliseconds(totalMs));
        using var client = Client(profile, transport, clock); var pending = client.GetAsync("https://owner.example/read");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); clock.Advance(TimeSpan.FromMilliseconds(attemptMs));
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => pending);
        Assert.Equal(expected, error.Outcome.Failure); Assert.Equal(1, transport.Attempts);
    }
    [Fact]
    public async Task FullResponseBodyIsInsideAttemptBudget_EvenHeadersReadMode()
    {
        var clock = new TestClock(); var body = new BlockingStream();
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var client = Client(new("test", Cp6HttpOperationKind.IdempotentRead, attemptTimeout: TimeSpan.FromMilliseconds(100), totalTimeout: TimeSpan.FromMilliseconds(500)), transport, clock);
        var pending = client.GetAsync("https://owner.example/read", HttpCompletionOption.ResponseHeadersRead);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); clock.Advance(TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => pending);
        Assert.Equal(Cp6HttpV2Failure.AttemptTimeout, error.Outcome.Failure);
    }
    [Fact]
    public async Task OversizedResponseStopsWithoutUnlimitedBuffering()
    {
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[200]) }));
        using var client = Client(new("test", Cp6HttpOperationKind.IdempotentRead, responseLimitBytes: 100), transport);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync("https://owner.example/read"));
        Assert.Equal(Cp6HttpV2Failure.ResponseLimitExceeded, error.Outcome.Failure); Assert.Equal(1, transport.Attempts);
    }
    [Fact]
    public async Task ParentBudgetWaitHonorsThisCallersCancellation()
    {
        var clock = new TestClock(); using var parent = new Cp6HttpDeadline(clock, TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(new("test", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent), transport, clock);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://owner.example/read"); request.Options.Set(Cp6HttpV2Handler.ParentDeadlineOption, parent);
        var pending = client.SendAsync(request, cancellation.Token); cancellation.Cancel();
        var completed = await Task.WhenAny(pending, Task.Delay(300));
        Assert.Same(pending, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Equal(1, transport.Attempts);
    }
    [Fact]
    public async Task CircuitCountsActualTransientAttempts_AndRejectsNextNewCallAsNotSent()
    {
        var transport = new Scripted((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(new("test", Cp6HttpOperationKind.IdempotentRead, circuitMinimumThroughput: 2), transport);
        using var first = await client.GetAsync("https://owner.example/read"); using var second = await client.GetAsync("https://owner.example/read");
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync("https://owner.example/read"));
        Assert.Equal(Cp6HttpV2Failure.CircuitOpen, error.Outcome.Failure);
        Assert.Equal(Cp6HttpOperationState.NotSent, error.Outcome.State); Assert.Equal(2, transport.Attempts);
    }
    internal static Cp6HttpV2Profile Profile(Cp6HttpOperationKind kind = Cp6HttpOperationKind.IdempotentRead,
        Cp6HttpRetryMode mode = Cp6HttpRetryMode.OwnerProvenIdempotent, int retries = 2) => new("test", kind, mode, retries, baseDelayMs: 0, maxDelayMs: 0);
    internal static HttpClient Client(Cp6HttpV2Profile profile, HttpMessageHandler transport, TimeProvider? clock = null,
        Cp6HttpOperationBinding? binding = null, ICp6HttpOperationAuthority? authority = null) =>
        new(new Cp6HttpV2Handler(profile, clock ?? TimeProvider.System, binding, authority, () => 0.5) { InnerHandler = transport }) { Timeout = Timeout.InfiniteTimeSpan };
    internal sealed class Scripted(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Attempts++; return action(request, cancellationToken); }
    }
    internal sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
