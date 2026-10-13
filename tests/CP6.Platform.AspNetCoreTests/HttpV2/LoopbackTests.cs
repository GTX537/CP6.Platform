using System.Net;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class LoopbackTests
{
    [Fact]
    public async Task RealTruncatedBodyIsTypedTransportFailureAfterHeadersWereObserved()
    {
        var headersObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            context.Response.ContentLength = 100;
            await context.Response.WriteAsync("partial"); await context.Response.Body.FlushAsync();
            await headersObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)); context.Abort();
        });
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler());
        var observedTransport = new HandlerTests.Scripted(async (request, token) =>
        {
            var response = await invoker.SendAsync(request, token); headersObserved.TrySetResult(); return response;
        });
        using var client = HandlerTests.Client(new("read", Cp6HttpOperationKind.IdempotentRead), observedTransport);
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync(server.Address));
        Assert.Equal(Cp6HttpV2Failure.Transport, error.Outcome.Failure); Assert.Equal(1, observedTransport.Attempts);
        Assert.True(headersObserved.Task.IsCompletedSuccessfully);
    }
    [Fact]
    public async Task RealReadRetriesAreBounded_AndNoProofWriteHasZeroRetries()
    {
        var reads = 0; var writes = 0;
        await using var server = await Server.Start(async context =>
        {
            if (context.Request.Method == "GET") context.Response.StatusCode = Interlocked.Increment(ref reads) < 3 ? 503 : 200;
            else { Interlocked.Increment(ref writes); context.Response.StatusCode = 503; }
            await context.Response.WriteAsync("loopback");
        });
        var services = new ServiceCollection();
        services.AddHttpClient("read", client => client.BaseAddress = server.Address)
            .AddCp6HttpResilience(new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent, baseDelayMs: 1, maxDelayMs: 1));
        services.AddHttpClient("write", client => client.BaseAddress = server.Address)
            .AddCp6HttpResilience(new Cp6HttpV2Profile("write", Cp6HttpOperationKind.IdempotentWrite));
        using var provider = services.BuildServiceProvider(); var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var read = factory.CreateClient("read"); using var result = await read.GetAsync("/"); Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.Equal(3, reads);
        using var write = factory.CreateClient("write"); using var request = new HttpRequestMessage(HttpMethod.Post, "/") { Content = new StringContent("same operation") };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "only-header-no-proof"); using var failed = await write.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode); Assert.Equal(1, writes);
    }
    [Fact]
    public async Task RealCallerCancellationDuringServerWaitHasNoLaterSend()
    {
        var count = 0; var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(context =>
        {
            Interlocked.Increment(ref count); context.Response.StatusCode = 503; context.Response.Headers.RetryAfter = "5";
            seen.TrySetResult(); return Task.CompletedTask;
        });
        using var transport = new SocketsHttpHandler();
        using var client = HandlerTests.Client(new("read", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent), transport);
        using var cancellation = new CancellationTokenSource(); var pending = client.GetAsync(server.Address, cancellation.Token);
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Equal(1, count);
    }
    [Fact]
    public async Task RealResponseHeadersDoNotEscapeTotalBodyDeadline()
    {
        var count = 0;
        await using var server = await Server.Start(async context =>
        {
            Interlocked.Increment(ref count); context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("start", context.RequestAborted); await context.Response.Body.FlushAsync(context.RequestAborted);
            try { await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        using var client = HandlerTests.Client(new("read", Cp6HttpOperationKind.IdempotentRead,
            attemptTimeout: TimeSpan.FromMilliseconds(250), totalTimeout: TimeSpan.FromMilliseconds(250)), new SocketsHttpHandler());
        var error = await Assert.ThrowsAsync<Cp6HttpV2Exception>(() => client.GetAsync(server.Address, HttpCompletionOption.ResponseHeadersRead));
        Assert.Equal(Cp6HttpV2Failure.TotalTimeout, error.Outcome.Failure); Assert.Equal(1, count);
    }
    [Fact]
    public async Task RealServerWaitAboveCapStopsImmediately()
    {
        var count = 0;
        await using var server = await Server.Start(context =>
        { Interlocked.Increment(ref count); context.Response.StatusCode = 429; context.Response.Headers.RetryAfter = "60"; return Task.CompletedTask; });
        using var client = HandlerTests.Client(new("read", Cp6HttpOperationKind.IdempotentRead, Cp6HttpRetryMode.OwnerProvenIdempotent,
            totalTimeout: TimeSpan.FromSeconds(100)), new SocketsHttpHandler());
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        using var response = await client.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, count); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(request.Options.TryGetValue(Cp6HttpV2Outcome.RequestOption, out var outcome));
        Assert.Equal(Cp6RetryStop.StopServerWaitExceeded, outcome.RetryStop);
    }
    private sealed class Server(WebApplication app) : IAsyncDisposable
    {
        public Uri Address { get; } = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        public static async Task<Server> Start(RequestDelegate action, X509Certificate2? certificate = null)
        {
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
            { if (certificate is not null) listen.UseHttps(certificate); }));
            var app = builder.Build(); app.Run(action); await app.StartAsync(); return new(app);
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }

    [Fact]
    public async Task OptInClientDoesNotFollowRedirectOrAcquireCookieCredentials()
    {
        var count = 0;
        await using var server = await Server.Start(context =>
        {
            Interlocked.Increment(ref count); context.Response.StatusCode = 307;
            context.Response.Headers.Location = "/redirect-target";
            context.Response.Headers.SetCookie = "credential=unexpected; Path=/";
            return Task.CompletedTask;
        });
        var services = new ServiceCollection();
        services.AddHttpClient("read").AddCp6HttpResilience(new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead));
        using var provider = services.BuildServiceProvider(); using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("read");
        using var response = await client.GetAsync(server.Address);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode); Assert.Equal(1, count);
    }

    [Fact]
    public async Task HttpsFixtureOnly_OwnerQueryPrecedesReplay_AndWireBytesAndKeyStayIdentical()
    {
        // This is an ephemeral library protocol fixture, not a durable Owner deduplication proof.
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); san.AddDnsName("localhost");
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        SocketsHttpHandler Transport() => new()
        {
            AllowAutoRedirect = false, UseCookies = false,
            SslOptions = new() { RemoteCertificateValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.GetCertHashString(HashAlgorithmName.SHA256) == fingerprint }
        };
        var wire = new ConcurrentQueue<(string Method, byte[] Body, string Key)>(); var posts = 0;
        await using var server = await Server.Start(async context =>
        {
            using var bytes = new MemoryStream(); await context.Request.Body.CopyToAsync(bytes, context.RequestAborted);
            wire.Enqueue((context.Request.Method, bytes.ToArray(), context.Request.Headers["Idempotency-Key"].ToString()));
            if (context.Request.Method == "GET") { await context.Response.WriteAsync("fixture-not-applied"); return; }
            context.Response.StatusCode = Interlocked.Increment(ref posts) == 1 ? 503 : 200;
            await context.Response.WriteAsync("fixture-write-response");
        }, certificate);
        using var queryClient = HandlerTests.Client(new("query", Cp6HttpOperationKind.IdempotentRead), Transport());
        var binding = OwnerProtocolFixtureTests.Binding(destination: server.Address);
        var authority = new OwnerProtocolFixtureTests.FixtureAuthority
        {
            Query = async (deadline, token) =>
            {
                using var query = new HttpRequestMessage(HttpMethod.Get, new Uri(server.Address, "/receipt"));
                query.Options.Set(Cp6HttpV2Handler.ParentDeadlineOption, deadline);
                using var result = await queryClient.SendAsync(query, token);
                return new Cp6OriginalResult(await result.Content.ReadAsStringAsync(token) == "fixture-not-applied"
                    ? Cp6OriginalResultState.NotAppliedProven : Cp6OriginalResultState.Unknown);
            }
        };
        var services = new ServiceCollection(); services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(Transport)
            .AddCp6HttpResilience(HandlerTests.Profile(Cp6HttpOperationKind.IdempotentWrite), binding, _ => authority);
        using var provider = services.BuildServiceProvider(); using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        using var request = OwnerProtocolFixtureTests.Request(binding); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var observed = wire.ToArray(); Assert.Equal(new[] { "POST", "GET", "POST" }, observed.Select(x => x.Method));
        Assert.All(observed.Where(x => x.Method == "POST"), x =>
        { Assert.Equal(new byte[] { 1, 2, 3 }, x.Body); Assert.Equal("original-key", x.Key); });
        Assert.Equal(["verify", "query", "authorize"], authority.Events);
    }
}
