using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests;

public sealed class RequestContextMiddlewareTests
{
    [Fact]
    public async Task TrustedResolver_EstablishesReadOnlyContext_ThenClearsIt()
    {
        var snapshot = ValidSnapshot();
        await using var provider = BuildProvider(snapshot);
        await using var scope = provider.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        IRequestContext? observed = null;
        var middleware = new RequestContextMiddleware(_ =>
        {
            observed = accessor.Current;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            new DefaultHttpContext(),
            scope.ServiceProvider.GetRequiredService<IRequestContextResolver>(),
            accessor);

        Assert.NotNull(observed);
        Assert.Equal(snapshot.TenantId, observed.TenantId);
        Assert.Equal(snapshot.CorrelationId, observed.CorrelationId);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task MissingContext_FailsClosed_AndDoesNotInvokeApplication()
    {
        await using var provider = BuildProvider(null);
        await using var scope = provider.CreateAsyncScope();
        var invoked = false;
        var middleware = new RequestContextMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetRequiredService<IRequestContextResolver>(),
            scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>());

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.False(invoked);
    }

    [Fact]
    public async Task EmptyTenant_FailsClosed_InsteadOfSelectingAnotherOrganization()
    {
        var snapshot = ValidSnapshot() with { TenantId = Guid.Empty };
        await using var provider = BuildProvider(snapshot);
        await using var scope = provider.CreateAsyncScope();
        var middleware = new RequestContextMiddleware(_ => throw new InvalidOperationException("must not run"));
        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetRequiredService<IRequestContextResolver>(),
            scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>());

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task BrowserTenantHeader_IsNeverReadByPlatformMiddleware()
    {
        var trustedTenant = Guid.NewGuid();
        var attackerTenant = Guid.NewGuid();
        var snapshot = ValidSnapshot() with { TenantId = trustedTenant };
        await using var provider = BuildProvider(snapshot);
        await using var scope = provider.CreateAsyncScope();
        Guid observedTenant = default;
        var accessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        var middleware = new RequestContextMiddleware(_ =>
        {
            observedTenant = accessor.Current!.TenantId;
            return Task.CompletedTask;
        });
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant"] = attackerTenant.ToString();

        await middleware.InvokeAsync(
            httpContext,
            scope.ServiceProvider.GetRequiredService<IRequestContextResolver>(),
            accessor);

        Assert.Equal(trustedTenant, observedTenant);
        Assert.NotEqual(attackerTenant, observedTenant);
    }

    private static ServiceProvider BuildProvider(RequestContextSnapshot? snapshot)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ResolutionSource(snapshot));
        services.AddCp6RequestContext<StubResolver>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static RequestContextSnapshot ValidSnapshot() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "subject", "audience", "correlation", "token", false);

    private sealed record ResolutionSource(RequestContextSnapshot? Snapshot);

    private sealed class StubResolver(ResolutionSource source) : IRequestContextResolver
    {
        public ValueTask<RequestContextSnapshot?> ResolveAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(source.Snapshot);
    }
}
