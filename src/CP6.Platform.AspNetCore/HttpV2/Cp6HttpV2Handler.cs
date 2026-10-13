using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Microsoft.Extensions.Http.Resilience;

namespace CP6.Platform.AspNetCore.HttpV2;

/// <summary>Total deadline encloses bounded retry, a Polly circuit, and individual attempt/body deadlines.</summary>
public sealed class Cp6HttpV2Handler : DelegatingHandler
{
    private readonly Cp6HttpV2Profile profile;
    private readonly TimeProvider clock;
    private readonly Cp6HttpOperationBinding? binding;
    private readonly ICp6HttpOperationAuthority? authority;
    private readonly ResiliencePipeline<HttpResponseMessage> circuit;
    private readonly Func<double> jitter;
    public static readonly HttpRequestOptionsKey<Cp6HttpDeadline> ParentDeadlineOption = new("CP6.HttpV2.ParentDeadline");

    internal Cp6HttpV2Handler(Cp6HttpV2Profile profile, TimeProvider clock,
        Cp6HttpOperationBinding? binding = null, ICp6HttpOperationAuthority? authority = null, Func<double>? jitter = null)
    {
        this.profile = profile; this.clock = clock; this.binding = binding; this.authority = authority;
        this.jitter = jitter ?? Random.Shared.NextDouble;
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = clock };
        circuit = builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            Name = "cp6-v2-circuit", FailureRatio = 0.5, SamplingDuration = profile.CircuitSamplingDuration,
            MinimumThroughput = profile.CircuitMinimumThroughput, BreakDuration = profile.CircuitBreakDuration,
            ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException ||
                args.Outcome.Result is { } response && IsTransient(response.StatusCode))
        }).Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var total = request.Options.TryGetValue(ParentDeadlineOption, out var parent)
            ? parent.CreateChild(profile.TotalTimeout, cancellationToken) : new Cp6HttpDeadline(clock, profile.TotalTimeout, cancellationToken);
        // All timestamp anchors use the budget's clock domain, including nested calls.
        var operationClock = total.TimeProvider;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, total.Token);
        var token = linked.Token;
        var write = profile.OperationKind != Cp6HttpOperationKind.IdempotentRead;
        var attempts = 0;
        var state = Cp6HttpOperationState.NotSent;
        var lastRetryAfterKind = Cp6RetryAfterKind.Absent;
        request.Options.TryGetValue(Cp6HttpOperationIdentity.RequestOption, out var operation);
        Cp6HttpV2Outcome Outcome(Cp6HttpV2Failure failure, Cp6RetryStop stop = Cp6RetryStop.None) =>
            new(state, failure, attempts, profile.PolicyVersion, operation, stop, lastRetryAfterKind);
        Cp6HttpV2Exception Failure(Cp6HttpV2Failure failure, Exception? inner = null, Cp6RetryStop stop = Cp6RetryStop.None)
        {
            var result = Outcome(failure, stop); request.Options.Set(Cp6HttpV2Outcome.RequestOption, result);
            return new(result, inner);
        }
        void EnsureTotalActive()
        {
            total.ThrowIfCallerCancellationRequested();
            token.ThrowIfCancellationRequested();
            if (total.Remaining == TimeSpan.Zero) throw Failure(Cp6HttpV2Failure.TotalTimeout);
        }
        if (write ? !IsWrite(request.Method) : !IsRead(request.Method)) throw Failure(Cp6HttpV2Failure.OperationNotAllowed);
        var canRetry = profile.RetryMode == Cp6HttpRetryMode.OwnerProvenIdempotent && profile.RetryAttempts > 0;
        var canQuery = false;
        byte[]? body = null;
        Cp6HttpRequestSnapshot? snapshot = null;
        var headers = request.Headers.Select(h => (h.Key, Values: h.Value.ToArray())).ToArray();
        var originalContent = request.Content;
        var contentHeaders = originalContent?.Headers.Select(h => (h.Key, Values: h.Value.ToArray())).ToArray();
        var method = request.Method; var uri = request.RequestUri; var version = request.Version; var versionPolicy = request.VersionPolicy;
        HttpResponseMessage? pendingResponse = null;
        try
        {
            total.ThrowIfCallerCancellationRequested();
            if (total.Remaining == TimeSpan.Zero) throw Failure(Cp6HttpV2Failure.TotalTimeout);
            var wantsProof = write && binding is not null && !binding.OneTimeCredentialExchange && authority is not null && operation is not null;
            var bodyVerifiable = true;
            if ((canRetry || wantsProof) && originalContent is not null)
            {
                // Never confer replayability by buffering a stream or arbitrary custom content implementation.
                if (originalContent is not ByteArrayContent || originalContent.Headers.ContentLength > profile.RequestReplayLimitBytes)
                { canRetry = false; bodyVerifiable = false; }
                else
                {
                    body = await originalContent.ReadAsByteArrayAsync(token).WaitAsync(token).ConfigureAwait(false);
                    if (body.Length > profile.RequestReplayLimitBytes) { canRetry = false; bodyVerifiable = false; }
                }
            }
            if (write && (canRetry || wantsProof && bodyVerifiable))
            {
                if (binding is null || authority is null || operation is null || binding.OneTimeCredentialExchange ||
                    operation.Binding != binding || binding.ClientName != profile.ClientName || binding.PolicyVersion != profile.PolicyVersion ||
                    method != binding.Method || uri != binding.Destination ||
                    !operation.MatchesBody(body ?? []) ||
                    !headers.Where(h => h.Key.Equals("Idempotency-Key", StringComparison.OrdinalIgnoreCase)).SelectMany(h => h.Values).SequenceEqual([operation.Key]))
                    throw Failure(Cp6HttpV2Failure.BindingRejected);
                snapshot = new Cp6HttpRequestSnapshot(method, uri!, headers, contentHeaders ?? [], body);
                var verified = await authority.VerifyBindingAsync(operation, snapshot, total, token).AsTask().WaitAsync(token).ConfigureAwait(false);
                EnsureTotalActive();
                if (!verified)
                    throw Failure(Cp6HttpV2Failure.BindingRejected);
                canQuery = true;
            }
            // The frozen transport snapshot is used on every send, including the first replayable attempt.
            for (var retry = 0; ; retry++)
            {
                EnsureTotalActive();
                if (total.Remaining < profile.AttemptTimeout && retry > 0) throw Failure(Cp6HttpV2Failure.TotalTimeout);
                HttpResponseMessage? response = null;
                Exception? failure = null;
                var category = Cp6HttpV2Failure.None;
                var observedUtc = operationClock.GetUtcNow(); var observedTimestamp = operationClock.GetTimestamp();
                var observedResponse = false;
                string[]? retryAfterValues = null;
                try
                {
                    response = await circuit.ExecuteAsync(async circuitToken =>
                    {
                        using var attempt = total.CreateChild(profile.AttemptTimeout);
                        using var attemptLinked = CancellationTokenSource.CreateLinkedTokenSource(circuitToken, attempt.Token);
                        var attemptToken = attemptLinked.Token;
                        HttpRequestMessage send = request;
                        if (canRetry || canQuery)
                        {
                            send = new HttpRequestMessage(method, uri) { Version = version, VersionPolicy = versionPolicy };
                            foreach (var header in headers) send.Headers.TryAddWithoutValidation(header.Key, header.Values);
                            if (body is not null)
                            {
                                send.Content = new ByteArrayContent(body);
                                foreach (var header in contentHeaders!) send.Content.Headers.TryAddWithoutValidation(header.Key, header.Values);
                            }
                        }
                        try
                        {
                            attemptToken.ThrowIfCancellationRequested();
                            if (retry > 0 && total.Remaining < profile.AttemptTimeout)
                                throw Failure(Cp6HttpV2Failure.TotalTimeout);
                            attempts++;
                            state = Cp6HttpOperationState.Unknown;
                            var pending = base.SendAsync(send, attemptToken);
                            HttpResponseMessage received;
                            try { received = await pending.WaitAsync(attemptToken).ConfigureAwait(false); }
                            catch
                            {
                                _ = pending.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); else _ = t.Exception; }, TaskScheduler.Default);
                                throw;
                            }
                            observedUtc = operationClock.GetUtcNow(); observedTimestamp = operationClock.GetTimestamp();
                            observedResponse = true;
                            retryAfterValues = received.Headers.TryGetValues("Retry-After", out var receivedValues) ? receivedValues.Take(2).ToArray() : null;
                            try
                            {
                                await BufferResponseAsync(received, attemptToken).ConfigureAwait(false);
                                EnsureTotalActive();
                                if (attempt.Remaining == TimeSpan.Zero) throw Failure(Cp6HttpV2Failure.AttemptTimeout);
                                return received;
                            }
                            catch { received.Dispose(); throw; }
                        }
                        catch (OperationCanceledException) when (!total.IsCallerCancellationRequested)
                        {
                            throw Failure(total.Remaining == TimeSpan.Zero ? Cp6HttpV2Failure.TotalTimeout :
                                attempt.Token.IsCancellationRequested || attempt.Remaining == TimeSpan.Zero ? Cp6HttpV2Failure.AttemptTimeout :
                                Cp6HttpV2Failure.UnknownTimeoutSource);
                        }
                        finally { if (!ReferenceEquals(send, request)) send.Dispose(); }
                    }, token).ConfigureAwait(false);
                }
                catch (BrokenCircuitException exception) { throw Failure(Cp6HttpV2Failure.CircuitOpen, exception); }
                catch (Cp6HttpV2Exception exception) { failure = exception; category = exception.Outcome.Failure; }
                catch (TimeoutRejectedException exception) { failure = exception; category = Cp6HttpV2Failure.UnknownTimeoutSource; }
                catch (HttpRequestException exception) { failure = exception; category = Cp6HttpV2Failure.Transport; }
                pendingResponse = response;
                total.ThrowIfCallerCancellationRequested();
                if (total.Remaining == TimeSpan.Zero) throw Failure(Cp6HttpV2Failure.TotalTimeout, failure);
                if (!write && response is not null) state = Cp6HttpOperationState.ResponseReceived;
                if (failure is not null && !observedResponse)
                { observedUtc = operationClock.GetUtcNow(); observedTimestamp = operationClock.GetTimestamp(); }

                var transient = failure is HttpRequestException || category == Cp6HttpV2Failure.AttemptTimeout ||
                    response is not null && IsTransient(response.StatusCode);
                if (response is not null && !transient)
                {
                    state = Cp6HttpOperationState.ResponseReceived;
                    request.Options.Set(Cp6HttpV2Outcome.RequestOption, Outcome(Cp6HttpV2Failure.None));
                    pendingResponse = null;
                    return response;
                }
                if (write && state == Cp6HttpOperationState.Unknown && canQuery && operation is not null && authority is not null)
                {
                    // Query is deliberately before new-effect authorization, including when retries are exhausted.
                    var original = await QueryAsync(operation, total, token).ConfigureAwait(false);
                    try { EnsureTotalActive(); }
                    catch { original.Response?.Dispose(); throw; }
                    if (original.State is Cp6OriginalResultState.Applied or Cp6OriginalResultState.Rejected or Cp6OriginalResultState.Pending && original.Response is not null)
                    {
                        response?.Dispose();
                        state = original.State == Cp6OriginalResultState.Applied ? Cp6HttpOperationState.Applied :
                            original.State == Cp6OriginalResultState.Rejected ? Cp6HttpOperationState.Rejected : Cp6HttpOperationState.Pending;
                        request.Options.Set(Cp6HttpV2Outcome.RequestOption, Outcome(Cp6HttpV2Failure.None));
                        pendingResponse = null;
                        return original.Response;
                    }
                    original.Response?.Dispose();
                    if (original.State != Cp6OriginalResultState.NotAppliedProven)
                    {
                        response?.Dispose(); throw Failure(Cp6HttpV2Failure.RecoveryUnavailable);
                    }
                    if (canRetry && transient && retry < profile.RetryAttempts)
                    {
                        var authorized = await authority.AuthorizeReplayAsync(operation, snapshot!, total, token).AsTask().WaitAsync(token).ConfigureAwait(false);
                        EnsureTotalActive();
                        if (!authorized) throw Failure(Cp6HttpV2Failure.ReplayDenied);
                    }
                }
                EnsureTotalActive();
                if (!canRetry || !transient || retry >= profile.RetryAttempts)
                {
                    if (response is not null)
                    {
                        request.Options.Set(Cp6HttpV2Outcome.RequestOption, Outcome(category, Cp6RetryStop.RetryLimit));
                        pendingResponse = null;
                        return response;
                    }
                    throw Failure(category, failure);
                }
                var server = Cp6RetryAfter.Parse(retryAfterValues, observedUtc);
                lastRetryAfterKind = server.Kind;
                var decision = Cp6RetryDecision.Create(profile, retry + 1, server, observedTimestamp, total, jitter());
                if (!await decision.WaitAsync(total, profile.AttemptTimeout).ConfigureAwait(false))
                {
                    EnsureTotalActive();
                    if (response is not null)
                    {
                        request.Options.Set(Cp6HttpV2Outcome.RequestOption, Outcome(category,
                            decision.Stop == Cp6RetryStop.None ? Cp6RetryStop.StopBudgetInsufficient : decision.Stop));
                        pendingResponse = null;
                        return response;
                    }
                    throw Failure(category, failure, decision.Stop == Cp6RetryStop.None ? Cp6RetryStop.StopBudgetInsufficient : decision.Stop);
                }
                response?.Dispose();
                pendingResponse = null;
            }
        }
        catch (OperationCanceledException) when (!total.IsCallerCancellationRequested)
        { throw Failure(total.Token.IsCancellationRequested || total.Remaining == TimeSpan.Zero ? Cp6HttpV2Failure.TotalTimeout : Cp6HttpV2Failure.UnknownTimeoutSource); }
        catch (OperationCanceledException)
        {
            request.Options.Set(Cp6HttpV2Outcome.RequestOption, Outcome(Cp6HttpV2Failure.None, Cp6RetryStop.Cancelled));
            throw;
        }
        finally { pendingResponse?.Dispose(); }
    }

    private async Task<Cp6OriginalResult> QueryAsync(Cp6HttpOperationIdentity operation, Cp6HttpDeadline total, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (total.Remaining == TimeSpan.Zero) throw new OperationCanceledException(total.Token);
        using var query = total.CreateChild(profile.AttemptTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, query.Token);
        Cp6OriginalResult? observed = null;
        void EnsureQueryActive()
        {
            token.ThrowIfCancellationRequested();
            if (total.Remaining == TimeSpan.Zero) throw new OperationCanceledException(total.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (query.Remaining == TimeSpan.Zero) throw new OperationCanceledException(query.Token);
        }
        try
        {
            // One bounded read per recovery decision; the adapter must not create its own unbounded retry loop.
            var pending = authority!.QueryOriginalAsync(operation, query, linked.Token).AsTask();
            try { observed = await pending.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch
            {
                _ = pending.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Response?.Dispose(); else _ = t.Exception; }, TaskScheduler.Default);
                throw;
            }
            EnsureQueryActive();
            if (observed.Response is not null)
            {
                await BufferResponseAsync(observed.Response, linked.Token).ConfigureAwait(false);
            }
            EnsureQueryActive();
            var result = observed;
            observed = null;
            return result;
        }
        catch (Exception)
        {
            token.ThrowIfCancellationRequested();
            if (total.Remaining == TimeSpan.Zero) throw new OperationCanceledException(total.Token);
            return new(Cp6OriginalResultState.Unknown);
        }
        finally { observed?.Response?.Dispose(); }
    }

    private async Task BufferResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        try { await ReadBufferedResponseAsync(response, token).ConfigureAwait(false); }
        catch (IOException exception)
        {
            // Direct stream reads surface IOException/HttpIOException rather than HttpRequestException.
            // Preserve the transport classification so a sent write still queries its original receipt.
            throw new HttpRequestException("The response body could not be read from the transport.", exception);
        }
    }

    private async Task ReadBufferedResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (response.Content is null) return;
        if (response.Content.Headers.ContentLength > profile.ResponseLimitBytes) throw BodyLimit();
        var headers = response.Content.Headers.Select(h => (h.Key, Values: h.Value.ToArray())).ToArray();
        var old = response.Content;
        using var stream = await old.ReadAsStreamAsync(token).WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, profile.ResponseLimitBytes + 1 - (int)memory.Length)), token)
                .AsTask().WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (read == 0) break;
            memory.Write(buffer, 0, read);
            if (memory.Length > profile.ResponseLimitBytes) throw BodyLimit();
        }
        response.Content = new ByteArrayContent(memory.ToArray());
        foreach (var header in headers) response.Content.Headers.TryAddWithoutValidation(header.Key, header.Values);
        old.Dispose();
    }
    private Cp6HttpV2Exception BodyLimit() => new(new(Cp6HttpOperationState.Unknown,
        Cp6HttpV2Failure.ResponseLimitExceeded, 0, profile.PolicyVersion));
    internal static bool IsRead(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;
    internal static bool IsWrite(HttpMethod method) => method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method == HttpMethod.Delete;
    internal static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
