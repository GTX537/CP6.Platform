using System.Diagnostics;
using System.Net;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests;

public sealed class OutboundCorrelationTests
{
    [Fact]
    public async Task OutboundHandler_ReplacesConflictingHeadersWithValidatedTraceIdentifier()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "request-correlation" };
        var accessor = new HttpContextAccessor { HttpContext = context };
        var transport = new CapturingHandler();
        using var fixture = ClientFixture.Create(transport, accessor);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        request.Headers.TryAddWithoutValidation(
            Cp6CorrelationMiddleware.HeaderName,
            new[] { "forged-one", "forged-two" });

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "request-correlation" }, transport.CorrelationValues);
    }

    [Fact]
    public async Task OutboundHandler_UsesTrustedRequestContextWhenTraceIdentifierIsInvalid()
    {
        using var requestServices = new ServiceCollection()
            .AddSingleton<IRequestContextAccessor>(
                new FixedRequestContextAccessor(new FixedRequestContext("trusted-context-correlation")))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "invalid correlation with spaces",
            RequestServices = requestServices
        };
        var accessor = new HttpContextAccessor { HttpContext = context };
        var transport = new CapturingHandler();
        using var fixture = ClientFixture.Create(transport, accessor);

        using var response = await fixture.Client.PostAsync("/orders", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "trusted-context-correlation" }, transport.CorrelationValues);
    }

    [Fact]
    public async Task OutboundHandler_GeneratesLowercaseGuidAndNeverUsesActivityTraceId()
    {
        var transport = new CapturingHandler();
        using var fixture = ClientFixture.Create(transport, new HttpContextAccessor());
        using var activity = new Activity("outbound-correlation-test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        using var response = await fixture.Client.PostAsync("/orders", new StringContent("{}"));

        var correlation = Assert.Single(transport.CorrelationValues);
        Assert.Matches("^[0-9a-f]{32}$", correlation);
        Assert.NotEqual(activity.TraceId.ToString(), correlation);
    }

    [Fact]
    public async Task OutboundHandler_RejectsInvalidRequestContextAndGeneratesSafeValue()
    {
        using var requestServices = new ServiceCollection()
            .AddSingleton<IRequestContextAccessor>(
                new FixedRequestContextAccessor(new FixedRequestContext("invalid context value")))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            TraceIdentifier = string.Empty,
            RequestServices = requestServices
        };
        var transport = new CapturingHandler();
        using var fixture = ClientFixture.Create(
            transport,
            new HttpContextAccessor { HttpContext = context });

        using var response = await fixture.Client.PostAsync("/orders", new StringContent("{}"));

        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(transport.CorrelationValues));
    }

    private sealed class ClientFixture(ServiceProvider provider, HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public static ClientFixture Create(CapturingHandler transport, IHttpContextAccessor contextAccessor)
        {
            var profile = new Cp6HttpResilienceProfile(
                "crm",
                Cp6HttpOperationKind.NonIdempotent);
            var services = new ServiceCollection();
            services.AddSingleton(contextAccessor);
            services.AddHttpClient("crm", client => client.BaseAddress = new Uri("https://example.test"))
                .ConfigurePrimaryHttpMessageHandler(() => transport)
                .AddCp6HttpResilience(profile);
            var provider = services.BuildServiceProvider();
            return new ClientFixture(
                provider,
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("crm"));
        }

        public void Dispose()
        {
            Client.Dispose();
            provider.Dispose();
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string[] CorrelationValues { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CorrelationValues = request.Headers.TryGetValues(
                Cp6CorrelationMiddleware.HeaderName,
                out var values)
                ? values.ToArray()
                : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FixedRequestContextAccessor(IRequestContext current) : IRequestContextAccessor
    {
        public IRequestContext Current { get; } = current;
    }

    private sealed record FixedRequestContext(string CorrelationId) : IRequestContext
    {
        public Guid TenantId { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public Guid? UserId => null;

        public string Subject => "subject";

        public string Audience => "audience";

        public string? TokenId => null;

        public bool IsPublic => false;
    }
}
