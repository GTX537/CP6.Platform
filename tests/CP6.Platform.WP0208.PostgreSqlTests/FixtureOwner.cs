using System.Data;
using System.Text;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Platform.WP0208.PostgreSqlTests;

public class TestDb(string connection) : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql(connection);
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddPostgreSqlOutboxV2("fixture");
        model.Entity<Authority>().ToTable("OwnerAuthority", "fixture").HasKey(x => x.RootId);
    }
}

// Fixture-owned authority is persisted and changed under the same Owner and route roots.
// This does not attest a production Main/CRM Owner adapter.
public sealed class Authority
{
    public string RootId { get; set; } = "";
    public long OwnerLock { get; set; }
    public long RouteLock { get; set; }
    public bool Read { get; set; } = true;
    public bool Write { get; set; } = true;
    public bool Hold { get; set; }
    public bool PolicyAvailable { get; set; } = true;
    public bool ReferenceClosed { get; set; } = true;
    public bool ConsumersComplete { get; set; } = true;
    public bool UnknownResolved { get; set; } = true;
    public string PolicyVersion { get; set; } = "fixture-policy-v1";
}

public sealed class FixtureOwner(PgFixture fixture, OutboxRootKeyV2 root) : IOutboxOwnerV2
{
    private static long _nextLock = 820000;
    public OutboxRootKeyV2 Root { get; } = root;
    public ITrustedContextAccessorV1 Trusted { get; set; } = null!;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);
    public string Mapping { get; set; } = OutboxEncodingV2.Mapping;
    public bool RegisterAuthorityFence { get; set; } = true;
    public string Worker { get; set; } = "fixture-worker";
    public static OutboxPolicyV2 Policy(string version = "fixture-policy-v1") => new(version, 10, 3,
        TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
    public async ValueTask<OutboxRegistrationV2> ResolveAsync(DbContext db, TrustedRequestContextV1 trusted, OutboxRootKeyV2 key, CancellationToken ct)
    {
        Assert.Equal(Root, key); Assert.Equal(Root.TenantId, trusted.Context.TenantId);
        var a = await db.Set<Authority>().AsNoTracking().SingleAsync(x => x.RootId == key.Id, ct);
        return new(key, "fixture", Worker, "fixture-publishers-v1", "fixture-migration-v1", Mapping,
            "fixture-writers-v1", "fixture-cutover-v1", [a.OwnerLock], a.RouteLock, Policy(a.PolicyVersion) with { LeaseDuration = LeaseDuration, PublishBudget = LeaseDuration / 12 },
            RegisterAuthorityFence ? [new("fixture", "OwnerAuthority", "RootId", key.Id)] : null);
    }
    public async ValueTask AuthorizeAsync(OutboxTransactionV2 tx, OutboxAccessV2 access, CancellationToken ct)
    {
        // A Serializable snapshot can predate advisory-lock acquisition. The authority row lock
        // converts concurrent revocation/hold updates into 40001 instead of using stale evidence.
        var authority = tx.Context.Set<Authority>().AsNoTracking();
        if (!tx.ReadOnly) authority = tx.Context.Set<Authority>().FromSqlInterpolated($"SELECT * FROM fixture.\"OwnerAuthority\" WHERE \"RootId\"={Root.Id} FOR UPDATE").AsNoTracking();
        var a = await authority.SingleAsync(x => x.RootId == Root.Id, ct);
        if (access == OutboxAccessV2.ReadReceipt ? !a.Read : !a.Write)
            throw new OutboxProtocolExceptionV2(access == OutboxAccessV2.ReadReceipt ? "FIXTURE_READ_DENIED" : "FIXTURE_WRITE_DENIED");
    }
    public async ValueTask<OutboxHandoverV2?> VerifyHandoverAsync(OutboxTransactionV2 tx, Guid switchId, CancellationToken ct)
    {
        var a = await tx.Context.Set<Authority>().AsNoTracking().SingleAsync(x => x.RootId == Root.Id, ct);
        var ids = await (from b in tx.Context.Set<OutboxLeaseBindingV2>() join n in tx.Context.Set<Cp6OutboxMessage>() on b.NativeId equals n.Id
            where b.RootId == Root.Id && (n.Status == Cp6OutboxStatus.Pending || n.Status == Cp6OutboxStatus.Dispatching) select n.Id).ToArrayAsync(ct);
        return new("fixture-handover:" + switchId, "fixture-publishers-v1", ids, a.UnknownResolved);
    }
    public async ValueTask<OutboxRetentionEvidenceV1?> RetentionEligibilityAsync(OutboxTransactionV2 tx, OutboxRetentionCandidateV1 candidate, CancellationToken ct)
    {
        var a = await tx.Context.Set<Authority>().AsNoTracking().SingleAsync(x => x.RootId == Root.Id, ct);
        return !a.PolicyAvailable ? null : new("fixture-retention", a.PolicyVersion, "fixture-terminal-owner-receipt", "fixture-consumers-v1",
            a.ConsumersComplete, a.ReferenceClosed, a.Hold, !a.UnknownResolved, DateTimeOffset.UnixEpoch, "fixture-dedup", "fixture-archive", true);
    }
    public async Task SetAuthority(Action<Authority> change)
    {
        await using var db = await fixture.OpenAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var a = await db.Set<Authority>().SingleAsync(x => x.RootId == Root.Id);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({a.OwnerLock})");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({a.RouteLock})");
        change(a); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task<Guid[]> Seed(int count = 1)
    {
        await using var db = await fixture.OpenAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var ownerLock = Interlocked.Add(ref _nextLock, 2);
        db.Add(new Authority { RootId = Root.Id, OwnerLock = ownerLock, RouteLock = ownerLock + 1 });
        db.Add(new OutboxRouteControlV2 { Id = Root.Id, OwnerId = Root.OwnerId, TenantId = Root.TenantId, QueueDomainId = Root.QueueDomainId,
            RouteId = Root.RouteId, CurrentEpoch = 1, Phase = OutboxPhaseV2.Active, LockKey = ownerLock + 1, PublisherSetReference = "fixture-publishers-v1" });
        var store = new Cp6OutboxStore<TestDb>(db, new Validator());
        var rows = Enumerable.Range(0, count).Select(i => store.Enqueue(new(Guid.NewGuid().ToString("N"), Root.TenantId,
            "fixture-topic", "fixture-partition", Encoding.UTF8.GetBytes(" {\"value\":1.00} \n"), "fixture-correlation", "fixture-causation", "fixture-aggregate", i + 1))).ToArray();
        await db.SaveChangesAsync(); // Initial native INSERT precedes binding; no bypass UPDATE.
        foreach (var n in rows) db.Add(new OutboxLeaseBindingV2 { NativeId = n.Id, RootId = Root.Id, MessageId = n.MessageId,
            RawEnvelopeSha256 = n.PayloadSha256, AssignedEpoch = 1 });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return rows.Select(x => x.Id).ToArray();
    }
    private sealed class Validator : ICp6OutboxEnvelopeValidator
    {
        public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope) => Cp6MessageValidationResult.Valid;
    }
}
