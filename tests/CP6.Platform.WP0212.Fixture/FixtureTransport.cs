using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Platform.EntityFramework.TransportV1;
using CP6.Platform.Messaging;
using CP6.Platform.Messaging.TransportV1;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.WP0212.Fixture;

internal sealed record ReceiveEvidence(string Topic,string Key,int Partition,long Offset);
internal sealed record SendEvidence(Cp6OutboxDispatchMessage Message,string RootId);
internal sealed record BrokerObservation(string Topic,string Key,int Partition,long Offset,byte[] Body,string Hash);

internal sealed class FixtureTransport(FixtureDatabase database) : ITransportAuthorityV1, IMessageTransportV1
{
    public const string Topic="cp6.platform.contract-example-changed.v1";
    public const string Route="wp0212-fixture-observe-v1";
    public static readonly Guid Tenant=Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static string Contracts=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../..","contracts"));
    private static readonly Cp6ContractBundle Bundle=Cp6ContractBundle.LoadForTransport(Contracts,"1.0.0","5a83e10f9454363d8e8c7f30fec7d84e1abd91347a7a1de3a6b5509790532344");
    public static Cp6CloudEventValidator Validator {get;}=new(Bundle);
    private readonly HttpClient http=new(){BaseAddress=new Uri("http://127.0.0.1:13500"),Timeout=TimeSpan.FromSeconds(15)};
    public TransportRegistryV1 Registry {get;private set;}=null!;
    public string Private=>Path.Combine(database.Directory,"transport");
    public string Token(string name)=>JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Private,"secrets.json"))).RootElement.GetProperty("tokens").GetProperty(name).GetString()!;
    public void Initialize()
    {
        var e=Bundle.Entries.Single();
        Registry=new([new(TransportRouteV1.GenericProfile,Route,e.EventType,e.SchemaId,e.SchemaVersion,e.SchemaSha256,Bundle,"platform","urn:cp6:platform",Topic)],1048576,1048576,this);
    }
    public static string Hash(ReadOnlySpan<byte> b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    public static byte[] Original(string messageId)
    {
        var e=Bundle.Entries.Single();var example=e.Examples.Single(x=>x.Name=="valid");
        var raw=File.ReadAllBytes(Bundle.GetAssetPath(example.Path));
        // Generate a NEW test fact once. Retries use the stored result bytes, never this function.
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(raw).Replace("evt-0001",messageId,StringComparison.Ordinal));
    }
    public BrokerObservation? Observation(ReceiveEvidence evidence,string hash)
    {
        foreach(var file in System.IO.Directory.EnumerateFiles(Private,"observed-*.json"))
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(file));
            foreach(var row in doc.RootElement.EnumerateArray())
            {
                if(row.GetProperty("owner").GetString()!=database.Owner || row.GetProperty("authenticatedPrincipal").GetString()!="observer" ||
                    row.GetProperty("source").GetString()!="Kafka4.3.1-console-consumer-byte-output") continue;
                var key=Encoding.UTF8.GetString(Convert.FromBase64String(row.GetProperty("keyBase64").GetString()!));
                var body=Convert.FromBase64String(row.GetProperty("bodyBase64").GetString()!);
                if(row.GetProperty("topic").GetString()==evidence.Topic && key==evidence.Key && row.GetProperty("partition").GetInt32()==evidence.Partition &&
                    row.GetProperty("offset").GetInt64()==evidence.Offset && Hash(body)==hash && row.GetProperty("bodySha256").GetString()==hash)
                    return new(evidence.Topic,key,evidence.Partition,evidence.Offset,body,hash);
            }
        }
        return null;
    }
    public async ValueTask<AuthenticatedTransportFactsV1?> AuthenticateAsync(TransportAuthenticationRequestV1 request,object evidence,CancellationToken ct)
    {
        if(request.Route.Route!=Route || request.Route.Profile!=TransportRouteV1.GenericProfile) return null;
        if(request.Purpose==TransportPurposeV1.Publish && evidence is SendEvidence send)
        {
            await using var db=await database.CreateDbContextAsync(ct);
            await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);
            var native=await db.Set<Cp6OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==send.Message.Id,ct);
            if(native is null || native.Status!=Cp6OutboxStatus.Dispatching || native.PayloadSha256!=request.RawEnvelopeSha256 ||
                native.MessageId!=send.Message.MessageId || !native.Payload.AsSpan().SequenceEqual(send.Message.Payload.Span)) return null;
            var attempts=await db.Set<OutboxSendAttemptV2>().AsNoTracking().Where(x=>x.RootId==send.RootId).ToArrayAsync(ct);
            var attempt=attempts.SingleOrDefault(x=>{
                var ticket=JsonSerializer.Deserialize<ClaimTicketV2>(x.OriginalTicket);
                return ticket?.NativeId==native.Id && ticket.RawEnvelopeSha256==native.PayloadSha256 && ticket.MessageId==native.MessageId;
            });
            if(attempt is null) return null;
            return new("platform",native.TenantId,"na",native.TopicName,native.PartitionKey,native.MessageId,native.AggregateId,native.AggregateVersion,
                native.CorrelationId,native.CausationId,"committed-send:"+attempt.MutationId,request.RawEnvelopeSha256,Cp6CloudEventCodec.StructuredContentType);
        }
        if(request.Purpose==TransportPurposeV1.Receive && evidence is ReceiveEvidence receive)
        {
            var record=Observation(receive,request.RawEnvelopeSha256);if(record is null) return null;
            // Independently read broker record, fixed tenant/region and dedicated topic writer ACL.
            // Native Kafka has no outer aggregate-version field; these fields are observed record
            // claims validated by schema, not falsely described as broker-generated metadata.
            using var doc=JsonDocument.Parse(record.Body);var e=doc.RootElement;
            return new("platform",Tenant,"na",record.Topic,record.Key,e.GetProperty("id").GetString()!,e.GetProperty("aggregateid").GetString()!,
                e.GetProperty("aggregateversion").GetInt32(),e.GetProperty("correlationid").GetString()!,e.GetProperty("causationid").GetString()!,
                $"kafka:{record.Topic}:{record.Partition}:{record.Offset}",record.Hash,Cp6CloudEventCodec.StructuredContentType);
        }
        return null;
    }
    public async ValueTask<TransportDecisionV1> PublishAsync(VerifiedTransportMessageV1 message,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,"/v1.0/publish/cp6-kafka-pubsub/"+Topic+"?metadata.rawPayload=true&metadata.partitionKey="+Uri.EscapeDataString(message.Facts.PartitionKey));
        req.Headers.Add("dapr-api-token",Token("dapr"));req.Content=new ByteArrayContent(message.CopyRawBytes());
        req.Content.Headers.ContentType=new(Cp6CloudEventCodec.StructuredContentType);
        using var res=await http.SendAsync(req,ct);
        return res.IsSuccessStatusCode ? TransportDecisionV1.Confirmed : TransportDecisionV1.Unknown;
    }
    public TransportOutboxPublisherV1 Publisher(string rootId,bool suppressAck)=>new(async (message,ct)=>
    {
        var q=await Registry.QualifyAsync(TransportRouteV1.GenericProfile,Route,message.Payload,new SendEvidence(message,rootId),TransportPurposeV1.Publish,ct);
        if(!q.IsQualified || q.Message is null) return new(OutboxTransportDecisionV2.DefinitelyRejected,"FIXTURE_QUALIFICATION",false);
        var outcome=await Registry.PublishAsync(q.Message,this,ct);
        if(suppressAck && outcome==TransportDecisionV1.Confirmed) return new(OutboxTransportDecisionV2.Unknown,"INJECTED_ACK_SUPPRESSION");
        return new(outcome==TransportDecisionV1.Confirmed ? OutboxTransportDecisionV2.Confirmed : OutboxTransportDecisionV2.Unknown);
    });
}
