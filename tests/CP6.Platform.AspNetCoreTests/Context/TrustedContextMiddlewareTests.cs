using CP6.Platform.Abstractions;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.Context;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests.Context;

public sealed class TrustedContextMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptInRegistration_SharesAccessor_AndIgnoresCallerTenantIssuerActor(bool legacyFirst)
    {
        var state = new HostFixture();
        await using var provider = Build(state, legacyFirst);
        await using var scope = provider.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        var evidence = scope.ServiceProvider.GetRequiredService<ITrustedContextAccessorV1>();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Headers["X-Tenant"] = Guid.NewGuid().ToString();
        http.Request.Headers["X-Issuer"] = "https://attacker.example";
        http.Request.QueryString = new QueryString("?actor=admin&schema=public");
        http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"tenantId\":\"attacker\",\"actor\":\"admin\"}"));
        var middleware = new TrustedRequestContextMiddlewareV1(_ =>
        {
            Assert.Equal(state.Input!.Source.Actor.TenantId, accessor.Current!.TenantId);
            Assert.Equal(state.Input.Source.Actor.Subject, accessor.Current.Subject);
            Assert.Same(accessor.Current, evidence.CurrentTrusted!.Context);
            Assert.Equal(state.Input.Source.Actor.Issuer, evidence.CurrentTrusted.Evidence.Issuer);
            Assert.Equal(0, http.Request.Body.Position);
            return Task.CompletedTask;
        });
        await Invoke(middleware, http, scope.ServiceProvider);
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Null(accessor.Current);
        Assert.Null(evidence.CurrentTrusted);
        Assert.Equal(1, state.Verifications);
        Assert.Equal(1, state.Resolutions);
    }

    [Theory]
    [InlineData("unauthenticated", 401)]
    [InlineData("invalid", 403)]
    [InlineData("unavailable", 503)]
    public async Task ResolutionFailures_FailClosedWithExplicitStatusAndNoSensitiveBody(string mode, int status)
    {
        var state = new HostFixture();
        if (mode == "unauthenticated") state.Input = null;
        if (mode == "invalid") state.Registration = state.Registration with { IsActive = false };
        if (mode == "unavailable") state.Failure = new(ContextResolutionFailureV1.AuthorityUnavailable);
        await using var provider = Build(state);
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await Invoke(new(_ => throw new InvalidOperationException("business must not run")), http, scope.ServiceProvider);
        Assert.Equal(status, http.Response.StatusCode);
        Assert.Equal(0, http.Response.Body.Length);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);
        Assert.Null(scope.ServiceProvider.GetRequiredService<ITrustedContextAccessorV1>().CurrentTrusted);
    }

    [Fact]
    public async Task HandlerTypedError_IsNotReclassifiedAsAuthenticationFailure()
    {
        await using var provider = Build(new());
        await using var scope = provider.CreateAsyncScope();
        var error = new ContextResolutionExceptionV1(ContextResolutionFailureV1.AuthorityUnavailable);
        var http = new DefaultHttpContext();
        Assert.Same(error, await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() =>
            Invoke(new(_ => throw error), http, scope.ServiceProvider)));
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrUnexpectedVerifierFailure_PropagatesAndClears(bool cancel)
    {
        using var cts = new CancellationTokenSource();
        var state = new HostFixture { VerifyAction = () =>
        {
            if (!cancel) throw new InvalidOperationException("verifier fixture failure");
            cts.Cancel();
        } };
        await using var provider = Build(state);
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };
        Task Run() => Invoke(new(_ => throw new InvalidOperationException("business must not run")), http, scope.ServiceProvider);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        else await Assert.ThrowsAsync<InvalidOperationException>(Run);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);
        Assert.Equal(0, state.Resolutions);
    }

    [Fact]
    public async Task ExplicitHealthBranch_BypassesBusinessContextWithoutInventingTenant()
    {
        var state = new HostFixture { Input = null };
        await using var provider = Build(state);
        await using var scope = provider.CreateAsyncScope();
        var app = new ApplicationBuilder(scope.ServiceProvider);
        app.Map("/health", health => health.Run(_ => Task.CompletedTask));
        app.UseCp6TrustedRequestContextV1();
        app.Run(_ => throw new InvalidOperationException("business must not run"));
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Path = "/health";
        await app.Build()(http);
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Equal(0, state.Verifications);
        Assert.Equal(0, state.Resolutions);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);
    }

    private static Task Invoke(TrustedRequestContextMiddlewareV1 middleware, HttpContext http, IServiceProvider provider) =>
        middleware.InvokeAsync(http, provider.GetRequiredService<TrustedContextExecutorV1>(),
            provider.GetRequiredService<IContextSourceVerifierV1<HttpContext>>());

    private static ServiceProvider Build(HostFixture state, bool legacyFirst = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        if (legacyFirst) services.AddCp6RequestContext<LegacyResolver>();
        services.AddCp6TrustedRequestContextV1<Verifier, Authority>();
        if (!legacyFirst) services.AddCp6RequestContext<LegacyResolver>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class HostFixture
    {
        public ContextResolutionInputV1? Input { get; set; }
        public ContextBindingRegistrationV1 Registration { get; set; }
        public ContextResolutionExceptionV1? Failure { get; set; }
        public Action? VerifyAction { get; init; }
        public int Verifications { get; set; }
        public int Resolutions { get; set; }
        public HostFixture()
        {
            var tenant = Guid.NewGuid();
            var actor = new ContextActorV1(tenant, "https://issuer.example/" + tenant, "human", ContextActorKindV1.Human, Guid.NewGuid());
            var now = DateTimeOffset.UtcNow;
            Input = new(new(ContextEntryKindV1.Http, "host", "browser-profile", "browser", "view", tenant,
                    "route-view", "correlation", ["view"]),
                new(actor, actor, "browser", "region", now.AddMinutes(-1), now.AddMinutes(5), "auth-ref", null, ["view"]));
            Registration = new(tenant, tenant, "host", "browser-profile", actor.Issuer, "region", "browser", "view",
                ContextEntryKindV1.Http, actor, actor, "registration", "1", now.AddHours(-1), now.AddHours(1), true, ["view"]);
        }
    }

    private sealed class Verifier(HostFixture state) : IContextSourceVerifierV1<HttpContext>
    {
        public ValueTask<ContextResolutionInputV1?> VerifyAsync(HttpContext input, CancellationToken cancellationToken = default)
        {
            Assert.Equal(input.RequestAborted, cancellationToken);
            state.Verifications++;
            state.VerifyAction?.Invoke();
            return ValueTask.FromResult(state.Input);
        }
    }

    private sealed class Authority(HostFixture state) : IContextBindingAuthorityV1
    {
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
            AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default)
        {
            state.Resolutions++;
            if (state.Failure is not null) throw state.Failure;
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(state.Registration);
        }
    }

    private sealed class LegacyResolver : IRequestContextResolver
    {
        public ValueTask<RequestContextSnapshot?> ResolveAsync(HttpContext httpContext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RequestContextSnapshot?>(null);
    }
}
