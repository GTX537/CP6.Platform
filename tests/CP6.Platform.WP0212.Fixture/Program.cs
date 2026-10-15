using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Platform.Messaging.TransportV1;
using CP6.Platform.WP0212.Fixture;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var settings=Environment.GetEnvironmentVariable("CP6_WP0212_SETTINGS") ?? throw new InvalidOperationException("OWNED_SETTINGS_REQUIRED");
var database=new FixtureDatabase(settings);
if(args.Contains("--initialize",StringComparer.Ordinal)) { await database.Initialize();Console.WriteLine("Owned fixture schema initialized; no business database changed.");return; }
var transport=new FixtureTransport(database);transport.Initialize();
var builder=WebApplication.CreateBuilder(args);builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:18012","http://127.0.0.1:18013");
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=1500000);
var app=builder.Build();
static bool Same(string expected,string actual)=>CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(actual));
app.Use(async (ctx,next)=>{
    if(ctx.Request.Path=="/healthz") {await next();return;}
    var sidecar=ctx.Request.Path.StartsWithSegments("/dapr") || ctx.Request.Path.StartsWithSegments("/events");
    var header=ctx.Request.Headers[sidecar ? "dapr-api-token" : "x-fixture-control"].ToString();
    if(!Same(transport.Token(sidecar ? "app" : "control"),header)){ctx.Response.StatusCode=401;return;}
    try {await next();} catch(Exception){ctx.Response.StatusCode=503;await ctx.Response.WriteAsJsonAsync(new{status="RETRY",safeCode="FIXTURE_DEPENDENCY_UNKNOWN"});}
});
app.MapGet("/healthz",()=>Results.Json(new{owner=database.Owner,scope="WP02-12 fixture only"}));
app.MapPost("/test/stop",()=>{app.Lifetime.StopApplication();return Results.Ok();});
app.MapGet("/dapr/subscribe",(HttpContext ctx)=>ctx.Connection.LocalPort==18013 ? Results.Json(Array.Empty<object>()) : Results.Json(new[]{new{pubsubname="cp6-kafka-pubsub",topic=FixtureTransport.Topic,route="events",metadata=new{rawPayload="true"}},
    new{pubsubname="cp6-kafka-pubsub",topic="cp6.wp0212.wrong-topic.v1",route="events",metadata=new{rawPayload="true"}}}));
app.MapPost("/events",async(HttpContext ctx)=>{
    using var stream=new MemoryStream();await ctx.Request.Body.CopyToAsync(stream,ctx.RequestAborted);
    using var wire=JsonDocument.Parse(stream.ToArray());
    if(!wire.RootElement.TryGetProperty("data_base64",out var encoded)) return Results.Json(new{status="RETRY",safeCode="RAW_DELIVERY_REQUIRED"});
    var bytes=Convert.FromBase64String(encoded.GetString()!);
    var topic=ctx.Request.Headers["__topic"].ToString();var key=ctx.Request.Headers["__key"].ToString();
    if(!int.TryParse(ctx.Request.Headers["__partition"],out var partition) || !long.TryParse(ctx.Request.Headers["__offset"],out var offset) || topic.Length==0 || key.Length==0)
        return Results.Json(new{status="RETRY",safeCode="NATIVE_METADATA_REQUIRED"});
    var evidence=new ReceiveEvidence(topic,key,partition,offset);
    var q=await transport.Registry.QualifyAsync(TransportRouteV1.GenericProfile,FixtureTransport.Route,bytes,evidence,TransportPurposeV1.Receive,ctx.RequestAborted);
    if(q.Message is null) return Results.Json(new{status="RETRY",safeCode=q.Diagnostics.FirstOrDefault()});
    var disposition="SUCCESS";
    if(!q.IsQualified)
    {
        var quarantine=await transport.Registry.QuarantineAsync(q.Message,"fixture-retain-original-v1",database,ctx.RequestAborted);
        if(quarantine!=QuarantineDecisionV1.Drop) return Results.Json(new{status="RETRY",safeCode="QUARANTINE_UNKNOWN"});
        disposition="DROP";
    }
    // This read-only qualification consumer commits an observation, not a business projection.
    // It cannot enable a real Owner effect. ACK records ONLY this explicit fixture commitment.
    await using var db=await database.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
    var hash=FixtureTransport.Hash(bytes);var qualification=q.IsQualified ? "ValidatedEnvelope" : q.Diagnostics.First();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO fixture.\"ReceiveObservation\" (\"Topic\",\"Partition\",\"Offset\",\"Key\",\"Hash\",\"Body\",\"Qualification\",\"Disposition\") VALUES ({topic},{partition},{offset},{key},{hash},{bytes},{qualification},{disposition}) ON CONFLICT DO NOTHING");
    await tx.CommitAsync();return Results.Json(new{status=disposition,scope="fixture-observation-only"});
});
app.MapPost("/test/dispatch/{mode}",async(string mode)=>{
    Check.That(mode is "confirmed" or "unknown","FIXED_TEST_MODE");
    var id="wp0212-"+Guid.NewGuid().ToString("N");var raw=FixtureTransport.Original(id);
    var root=new OutboxRootKeyV2("wp0212-fixture",FixtureTransport.Tenant,"fixture-outbox",id);
    var owner=new FixtureOwner(root);var identity=new FixtureIdentity();
    var nativeId=await owner.Seed(database,raw);
    IReadOnlyList<OutboxDispatchObservationV2>? observation=null;var request=Guid.NewGuid();
    await identity.Run(async()=>{
        var dispatcher=new OutboxDispatcherV2<FixtureDb>(database,identity.Accessor,owner,transport.Publisher(root.Id,mode=="unknown"));
        observation=await dispatcher.DispatchBatchV2(root,request,1);
    });
    await using var db=await database.CreateDbContextAsync();var native=await db.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x=>x.Id==nativeId);
    var result=new{messageId=id,rawSha256=FixtureTransport.Hash(raw),nativeId,root,request,observation,status=native.Status.ToString(),businessApplied=false,
        faultInjection=mode=="unknown" ? "application suppressed successful Dapr ACK" : "none"};
    await File.WriteAllBytesAsync(Path.Combine(transport.Private,id+"-dispatch.json"),JsonSerializer.SerializeToUtf8Bytes(result));
    return Results.Json(result);
});
app.MapGet("/test/evidence",async()=>{
    await using var db=await database.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
    return Results.Json(new{receives=await db.Set<ReceiveObservation>().AsNoTracking().OrderBy(x=>x.Offset).ToArrayAsync(),
        quarantine=await db.Set<RawQuarantine>().AsNoTracking().ToArrayAsync(),businessApplied=false});
});
await app.RunAsync();
