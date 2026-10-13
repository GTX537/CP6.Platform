using System.Text;
using System.Text.Json;
using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class SafeProblemTests
{
    [Fact]
    public async Task RegisteredTextOnly_TraceAndCorrelationAreSafe_AndInputNeverEchoes()
    {
        var policy = Policy(); using var provider = new ServiceCollection().AddCp6ProblemDetails(policy).BuildServiceProvider();
        var context = Context(provider, policy); context.TraceIdentifier = "token\r\nsecret";
        Assert.Equal(Cp6SafeProblemWriteResult.Written, await context.WriteCp6ProblemAsync(policy, new Dictionary<string, string[]> { ["name"] = ["required"] }));
        var text = Body(context); using var json = JsonDocument.Parse(text);
        Assert.Equal("Name is required.", json.RootElement.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Matches("^[a-f0-9]{32}$", json.RootElement.GetProperty("traceId").GetString()!);
        Assert.DoesNotContain("secret", text); Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(422, context.Response.StatusCode); Assert.DoesNotContain("detail", text); Assert.DoesNotContain("instance", text);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HiddenFieldIncludingEmptyMessagesFallsBackWithoutEcho(bool empty)
    {
        var policy = Policy(); using var provider = new ServiceCollection().AddCp6ProblemDetails(policy).BuildServiceProvider();
        var context = Context(provider, policy);
        Assert.Equal(Cp6SafeProblemWriteResult.GenericFallback, await context.WriteCp6ProblemAsync(policy,
            new Dictionary<string, string[]> { ["secret-value-in-field"] = empty ? [] : ["required"] }));
        Assert.DoesNotContain("secret", Body(context));
    }
    [Fact]
    public async Task ArbitraryExceptionOrUserTextCannotBecomeRegisteredMessage()
    {
        var policy = Policy(); using var provider = new ServiceCollection().AddCp6ProblemDetails(policy).BuildServiceProvider();
        var context = Context(provider, policy);
        Assert.Equal(Cp6SafeProblemWriteResult.GenericFallback, await context.WriteCp6ProblemAsync(policy,
            new Dictionary<string, string[]> { ["name"] = ["SQL password=secret"] }));
        Assert.DoesNotContain("SQL", Body(context));
    }
    [Theory]
    [InlineData(33, 1, 1)] [InlineData(1, 5, 1)] [InlineData(1, 1, 257)] [InlineData(32, 4, 256)]
    public async Task CountCharacterAndSerializedUtf8LimitsProduceWholeGenericJson(int fieldCount, int messages, int length)
    {
        var fields = Enumerable.Range(0, fieldCount).ToDictionary(i => "f" + i,
            _ => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["x"] = new('界', length) });
        var policy = new Cp6SafeProblemPolicy("bounded", Definition(), fields);
        using var provider = new ServiceCollection().AddCp6ProblemDetails(policy).BuildServiceProvider();
        var context = Context(provider, policy);
        var errors = fields.Keys.ToDictionary(k => k, _ => Enumerable.Repeat("x", messages).ToArray());
        Assert.Equal(Cp6SafeProblemWriteResult.GenericFallback, await context.WriteCp6ProblemAsync(policy, errors));
        var body = Body(context); Assert.True(Encoding.UTF8.GetByteCount(body) <= 16384);
        using var json = JsonDocument.Parse(body); Assert.Equal("CP6_SAFE_ERROR", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
    }
    [Fact]
    public async Task ForeignOrUnregisteredPolicyCannotBeBorrowedByEndpoint()
    {
        var policy = Policy(); var foreign = Policy();
        using var provider = new ServiceCollection().AddCp6ProblemDetails(policy).BuildServiceProvider();
        var context = Context(provider, policy);
        Assert.Equal(Cp6SafeProblemWriteResult.PolicyNotBound, await context.WriteCp6ProblemAsync(foreign));
        Assert.Equal(0, context.Response.Body.Length);
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(foreign), "foreign"));
        Assert.Equal(Cp6SafeProblemWriteResult.PolicyNotBound, await context.WriteCp6ProblemAsync(foreign));
    }
    [Fact]
    public async Task StartedAndAbortedResponsesNeverAppendSecondBody()
    {
        var policy = Policy(); using var provider = new ServiceCollection().AddCp6ProblemDetails(policy).BuildServiceProvider();
        var context = Context(provider, policy); context.Features.Set<IHttpResponseFeature>(new StartedFeature());
        Assert.Equal(Cp6SafeProblemWriteResult.ResponseStarted, await context.WriteCp6ProblemAsync(policy));
        var aborted = Context(provider, policy); aborted.RequestAborted = new CancellationToken(true);
        Assert.Equal(Cp6SafeProblemWriteResult.RequestAborted, await aborted.WriteCp6ProblemAsync(policy));
        Assert.Equal(0, aborted.Response.Body.Length);
    }
    [Fact]
    public async Task RegistrationSnapshotsDictionary_AndIsIdempotentOnlyForSamePolicy()
    {
        var texts = new Dictionary<string, string> { ["required"] = "Name is required." };
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["name"] = texts };
        var policy = new Cp6SafeProblemPolicy("p", Definition(), fields); texts["required"] = "secret"; fields.Clear();
        var services = new ServiceCollection().AddCp6ProblemDetails(policy); var count = services.Count;
        services.AddCp6ProblemDetails(policy); Assert.Equal(count, services.Count);
        Assert.Throws<InvalidOperationException>(() => services.AddCp6ProblemDetails(new Cp6SafeProblemPolicy("p", Definition(), fields)));
        using var provider = services.BuildServiceProvider(); var context = Context(provider, policy);
        await context.WriteCp6ProblemAsync(policy, new Dictionary<string, string[]> { ["name"] = ["required"] });
        Assert.Contains("Name is required.", Body(context)); Assert.DoesNotContain("secret", Body(context));
    }
    private static Cp6ProblemDefinition Definition() => new("https://errors.example/invalid", "Invalid input", 422, "INPUT_INVALID", "app.error.invalid");
    private static Cp6SafeProblemPolicy Policy() => new("endpoint", Definition(),
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["name"] = new Dictionary<string, string> { ["required"] = "Name is required." } });
    private static DefaultHttpContext Context(IServiceProvider provider, Cp6SafeProblemPolicy policy)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(policy), "test")); return context;
    }
    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    private sealed class StartedFeature : HttpResponseFeature { public override bool HasStarted => true; }
}
