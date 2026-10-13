using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.InboxV1;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.WP0207.PostgreSqlTests;

// This fixture owns its own journal/projection and seven existing native lock roots.
// It is NOT a Main/CRM business adapter, schema catalogue, or production final authorization gate.
public sealed class FixtureOwner : ICp6InboxPreCheckpointV1, IInboxResultReaderV1
{
    public bool Available { get; set; } = true;
    public bool AllowWrite { get; set; } = true;
    public bool AllowRead { get; set; } = true;
    public bool FailAfterSave { get; set; }
    public bool BadLockOrder { get; set; }
    public int Evaluations { get; private set; }
    public int Applications { get; private set; }
    public Func<InboxTransactionV1, Task>? AfterAuthorityRead { get; set; }
    public Func<InboxTransactionV1, Task>? AfterOwnerWrite { get; set; }
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static string Key(Cp6InboxDelivery d) => d.AggregateId + ":" + d.AggregateVersion;
    public static string InputDigest(Cp6InboxDelivery d)
    {
        using var doc = JsonDocument.Parse(d.Payload);
        return Hash(Encoding.UTF8.GetBytes("fixture-owner-canonical-v1:" + Key(d) + ":" + JsonSerializer.Serialize(doc.RootElement.GetProperty("data"))));
    }
    public static Cp6InboxDelivery Delivery(Guid tenant, string? aggregate = null, string? message = null, int version = 1, int value = 7, bool whitespace = false)
    {
        aggregate ??= Guid.NewGuid().ToString("N"); message ??= Guid.NewGuid().ToString("N");
        var raw = JsonSerializer.Serialize(new { tenant, message, aggregate, version, type = "fixture.identity", data = new { value } });
        if (whitespace) raw = " \n" + raw + "\n";
        return new("fixture-consumer", message, tenant, "fixture-topic", "fixture-partition", Encoding.UTF8.GetBytes(raw), aggregate, version);
    }
    public ValueTask<InboxLockPlanV1> GetLockPlanAsync(InboxTransactionV1 tx, Cp6InboxDelivery delivery, InboxTransportV1? original, CancellationToken ct)
    {
        var locks = Enum.GetValues<InboxLockStageV1>().Select((s, i) => new InboxLockV1(s, 420001 + i)).ToArray();
        if (BadLockOrder) (locks[2], locks[3]) = (locks[3], locks[2]);
        return ValueTask.FromResult(new InboxLockPlanV1("fixture-shared-writer-advisory-v1", "fixture-writers-v1", locks));
    }
    public ValueTask<InboxContractBindingV1> ValidateContractAsync(InboxTransactionV1 tx, Cp6InboxDelivery delivery, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(delivery.Payload);
        var r = doc.RootElement;
        Assert.Equal(delivery.TenantId, r.GetProperty("tenant").GetGuid());
        Assert.Equal(delivery.MessageId, r.GetProperty("message").GetString());
        Assert.Equal(delivery.AggregateId, r.GetProperty("aggregate").GetString());
        Assert.Equal(delivery.AggregateVersion, r.GetProperty("version").GetInt32());
        Assert.Equal("fixture.identity", r.GetProperty("type").GetString());
        return ValueTask.FromResult(new InboxContractBindingV1("fixture-owner", Key(delivery), "1", "fixture-owner-input", "fixture-canonical-v1",
            InputDigest(delivery), "fixture.identity", "fixture-source", "region", "fixture-schema-v1", "fixture-bundle-v1", "fixture-registration-v1"));
    }
    public async ValueTask<InboxAuthoritySnapshotV1> ReadAuthorityAsync(InboxTransactionV1 tx, InboxInputV1 input, CancellationToken ct)
    {
        Evaluations++;
        var d = input.Delivery;
        var p = await tx.Context.Set<Projection>().AsNoTracking().SingleOrDefaultAsync(x => x.Tenant == tx.TenantId && x.Aggregate == d.AggregateId, ct);
        var history = await tx.Context.Set<Observation>().AsNoTracking().Where(x => x.Tenant == tx.TenantId && x.Aggregate == d.AggregateId)
            .Select(x => new InboxAuthorityVersionV1(x.Tenant, x.Aggregate, x.Version, x.Type, x.DataHash, "original-observation:" + x.Id)).ToArrayAsync(ct);
        if (AfterAuthorityRead is not null) await AfterAuthorityRead(tx);
        return new(Available, p is null ? null : new(p.Tenant, p.Aggregate, p.Version, p.EventType, p.DataHash, p.SourceRef), history);
    }
    public async ValueTask<InboxOwnerResultV1> ApplyDecisionAsync(InboxTransactionV1 tx, InboxInputV1 input,
        PreCheckpointIdentityDecisionV1 decision, InboxTransportV1? original, CancellationToken ct)
    {
        Applications++;
        var d = input.Delivery;
        var conflict = decision is PreCheckpointIdentityDecisionV1.TransportIdentityConflict or PreCheckpointIdentityDecisionV1.SameVersionConflict;
        var affectedAggregate = decision == PreCheckpointIdentityDecisionV1.TransportIdentityConflict && original is not null ? original.Aggregate : d.AggregateId;
        var p = await tx.Context.Set<Projection>().SingleOrDefaultAsync(x => x.Tenant == tx.TenantId && x.Aggregate == affectedAggregate, ct);
        var pub = await tx.Context.Set<Published>().SingleOrDefaultAsync(x => x.Tenant == tx.TenantId && x.Aggregate == affectedAggregate, ct);
        if (conflict)
        {
            if (p is not null) p.Drift = true;
            if (pub is not null) pub.Allow = false;
        }
        if (decision == PreCheckpointIdentityDecisionV1.FreshCandidate)
        {
            if (!AllowWrite) throw new InboxProtocolExceptionV1("FIXTURE_FINAL_WRITE_DENIED");
            if (p is null)
            {
                p = new Projection { Tenant = tx.TenantId, Aggregate = d.AggregateId };
                tx.Context.Add(p);
            }
            p.Version = d.AggregateVersion; p.DataHash = input.DataPayloadSha256; p.EventType = input.Binding.EventType;
            p.SourceRef = "original-native-event:" + d.MessageId;
            if (pub is null) { pub = new Published { Tenant = tx.TenantId, Aggregate = d.AggregateId }; tx.Context.Add(pub); }
            pub.Allow = !p.Drift;
            tx.Context.Add(new BusinessEffect { Tenant = tx.TenantId, Key = input.Binding.OperationKey, Count = 1 });
        }
        var observation = new Observation { Id = Guid.NewGuid(), Tenant = tx.TenantId, Aggregate = d.AggregateId,
            Version = d.AggregateVersion, Type = input.Binding.EventType, DataHash = input.DataPayloadSha256,
            Decision = decision.ToString(), NativeIdentity = tx.NativeIdentity };
        tx.Context.Add(observation);
        var key = input.Binding.OperationKey;
        var journal = await tx.Context.Set<Journal>().SingleOrDefaultAsync(x => x.Tenant == tx.TenantId && x.Key == key, ct);
        if (!conflict)
        {
            if (journal is null)
            {
                journal = new Journal { Tenant = tx.TenantId, Key = key, InputHash = input.Binding.InputDigest,
                    Result = Encoding.UTF8.GetBytes(" {\"original\":\"" + key + "\",\"amount\":1.00} \n") };
                tx.Context.Add(journal);
            }
            Assert.Equal(input.Binding.InputDigest, journal.InputHash);
        }
        if (AfterOwnerWrite is not null) await AfterOwnerWrite(tx);
        if (FailAfterSave)
        {
            await tx.Context.SaveChangesAsync(ct);
            throw new InboxProtocolExceptionV1("FIXTURE_FAILURE_AFTER_OWNER_SAVE");
        }
        return conflict ? new("conflict:" + observation.Id, ReadOnlyMemory<byte>.Empty) : new("owner-journal:" + key, journal!.Result);
    }
    public ValueTask<ReadOnlyMemory<byte>> AuthorizeAndFilterAsync(InboxTransactionV1 tx, InboxDeliveryOutcomeV1 outcome,
        InboxOriginalResultV1? original, CancellationToken ct)
    {
        if (!AllowRead) throw new InboxProtocolExceptionV1("FIXTURE_CURRENT_READ_DENIED");
        Assert.Equal(tx.TenantId, outcome.TenantId);
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(original?.Result ?? []);
    }
}
