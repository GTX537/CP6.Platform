using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using CP6.Platform.Messaging;
using System.Text.Json;

namespace CP6.Platform.WP0212.Fixture;

// Only this test Owner. No Main/CRM authorization or business transaction is claimed.
internal sealed class FixtureOwner(OutboxRootKeyV2 root) : IOutboxOwnerV2
{
    public OutboxRootKeyV2 Root => root;
    public async ValueTask<OutboxRegistrationV2> ResolveAsync(DbContext db,TrustedRequestContextV1 trusted,OutboxRootKeyV2 key,CancellationToken ct)
    {
        Check.That(key==root && trusted.Context.TenantId==root.TenantId,"FIXTURE_TENANT");
        var a=await db.Set<AuthorityRow>().AsNoTracking().SingleAsync(x=>x.RootId==root.Id,ct);
        return new(root,"fixture","fixture-worker","wp0212-publisher-v1","fixture-migration-v1",OutboxEncodingV2.Mapping,
            "fixture-writers-v1","fixture-cutover-v1",[a.OwnerLock],a.RouteLock,
            new("fixture-policy-v1",1,3,TimeSpan.FromMinutes(3),TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(30),TimeSpan.FromMinutes(5)),
            [new("fixture","OwnerAuthority","RootId",root.Id)]);
    }
    public async ValueTask AuthorizeAsync(OutboxTransactionV2 tx,OutboxAccessV2 access,CancellationToken ct)
    {
        var query=tx.Context.Set<AuthorityRow>().AsNoTracking();
        if(!tx.ReadOnly) query=tx.Context.Set<AuthorityRow>().FromSqlInterpolated($"SELECT * FROM fixture.\"OwnerAuthority\" WHERE \"RootId\"={root.Id} FOR UPDATE").AsNoTracking();
        var row=await query.SingleAsync(x=>x.RootId==root.Id,ct);
        Check.That(access==OutboxAccessV2.ReadReceipt || row.Write,"FIXTURE_REVOKED");
    }
    public ValueTask<OutboxHandoverV2?> VerifyHandoverAsync(OutboxTransactionV2 tx,Guid id,CancellationToken ct)=>ValueTask.FromResult<OutboxHandoverV2?>(null);
    public ValueTask<OutboxRetentionEvidenceV1?> RetentionEligibilityAsync(OutboxTransactionV2 tx,OutboxRetentionCandidateV1 candidate,CancellationToken ct)=>ValueTask.FromResult<OutboxRetentionEvidenceV1?>(null);
    public async Task<Guid> Seed(FixtureDatabase database,byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes);var e=doc.RootElement;
        await using var db=await database.CreateDbContextAsync();await using var tx=await db.Database.BeginTransactionAsync();
        var lockId=DateTime.UtcNow.Ticks;
        db.Add(new AuthorityRow{RootId=root.Id,OwnerLock=lockId,RouteLock=lockId+1});
        db.Add(new OutboxRouteControlV2{Id=root.Id,OwnerId=root.OwnerId,TenantId=root.TenantId,QueueDomainId=root.QueueDomainId,RouteId=root.RouteId,
            CurrentEpoch=1,Phase=OutboxPhaseV2.Active,LockKey=lockId+1,PublisherSetReference="wp0212-publisher-v1"});
        var store=new Cp6OutboxStore<FixtureDb>(db,new EnvelopeValidator());
        var row=store.Enqueue(new(e.GetProperty("id").GetString()!,root.TenantId,FixtureTransport.Topic,
            root.TenantId.ToString("D")+"/"+e.GetProperty("aggregateid").GetString(),bytes,
            e.GetProperty("correlationid").GetString()!,e.GetProperty("causationid").GetString()!,e.GetProperty("aggregateid").GetString()!,e.GetProperty("aggregateversion").GetInt32()));
        await db.SaveChangesAsync();
        db.Add(new OutboxLeaseBindingV2{NativeId=row.Id,RootId=root.Id,MessageId=row.MessageId,RawEnvelopeSha256=row.PayloadSha256,AssignedEpoch=1});
        await db.SaveChangesAsync();await tx.CommitAsync();return row.Id;
    }
    private sealed class EnvelopeValidator : ICp6OutboxEnvelopeValidator
    {
        public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope e)
        {
            var result=FixtureTransport.Validator.Validate(e.Payload);
            return result.IsValid ? Cp6MessageValidationResult.Valid : Cp6MessageValidationResult.Invalid();
        }
    }
}

internal sealed class FixtureIdentity : IContextSourceVerifierV1<object>,IContextBindingAuthorityV1
{
    private readonly object capability=new();
    public ContextScopeAccessorV1 Accessor {get;}=new();
    private ContextActorV1 Actor=>new(FixtureTransport.Tenant,"https://fixture.invalid/issuer","wp0212-worker",ContextActorKindV1.Service,null);
    public ValueTask<ContextResolutionInputV1?> VerifyAsync(object input,CancellationToken ct=default)
    {
        if(!ReferenceEquals(input,capability)) return ValueTask.FromResult<ContextResolutionInputV1?>(null);
        var now=DateTimeOffset.UtcNow;
        return ValueTask.FromResult<ContextResolutionInputV1?>(new(
            new(ContextEntryKindV1.Message,"fixture","wp0212-outbox","fixture","send",FixtureTransport.Tenant,"native","fixture-correlation",["send"]),
            new(Actor,Actor,"fixture","na",now.AddSeconds(-1),now.AddMinutes(10),"fixture-authenticated-control",null,["send"])));
    }
    public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 op,AuthenticatedContextSourceV1 source,CancellationToken ct=default)=>
        ValueTask.FromResult<ContextBindingRegistrationV1?>(new(FixtureTransport.Tenant,FixtureTransport.Tenant,"fixture","wp0212-outbox",Actor.Issuer,
            "na","fixture","send",ContextEntryKindV1.Message,Actor,Actor,"fixture-registration","1",DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddMinutes(10),true,["send"]));
    public Task Run(Func<Task> action)=>new TrustedContextExecutorV1(this,Accessor).ExecuteAsync(ContextEntryKindV1.Message,capability,this,_=>action());
}
