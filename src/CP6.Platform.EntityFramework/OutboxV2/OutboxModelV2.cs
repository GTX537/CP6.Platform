using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace CP6.Platform.EntityFramework.OutboxV2;

public sealed class OutboxRouteControlV2
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public Guid TenantId { get; set; }
    public string QueueDomainId { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string ProfileId { get; set; } = OutboxEncodingV2.Profile;
    public int ProfileVersion { get; set; } = 1;
    public long CurrentEpoch { get; set; }
    public OutboxPhaseV2 Phase { get; set; }
    public long LockKey { get; set; }
    public string PublisherSetReference { get; set; } = "";
    public Guid? LastSwitchId { get; set; }
    public uint Version { get; set; }
}
public sealed class OutboxLeaseBindingV2
{
    public Guid NativeId { get; set; }
    public string RootId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string RawEnvelopeSha256 { get; set; } = "";
    public long AssignedEpoch { get; set; }
    public Guid? CurrentClaimId { get; set; }
    public long? ClaimEpoch { get; set; }
    public DateTimeOffset? ClaimStartedAtUtc { get; set; }
    public uint Version { get; set; }
}
/// <summary>One immutable batch request, including empty results. Receipt JSON contains per-native immutable tickets.</summary>
public sealed class OutboxClaimReceiptV2
{
    public string RootId { get; set; } = "";
    public Guid RequestId { get; set; }
    public string Input { get; set; } = "";
    public string Tickets { get; set; } = "[]";
    public string WriterTransaction { get; set; } = "";
}
public sealed class OutboxLeaseMutationReceiptV2
{
    public string RootId { get; set; } = "";
    public Guid MutationId { get; set; }
    public Guid NativeId { get; set; }
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public string WriterTransaction { get; set; } = "";
}
/// <summary>Per-native immutable claim receipt; batch header additionally preserves empty requests.</summary>
public sealed class OutboxClaimTicketReceiptV2
{
    public string RootId { get; set; } = "";
    public Guid RequestId { get; set; }
    public Guid NativeId { get; set; }
    public Guid ReceiptReference { get; set; }
    public string Ticket { get; set; } = "";
    public string WriterTransaction { get; set; } = "";
}
public sealed class OutboxAuditV2
{
    public Guid Id { get; set; }
    public string RootId { get; set; } = "";
    public Guid? NativeId { get; set; }
    public string Kind { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTimeOffset AtUtc { get; set; }
}
/// <summary>Transaction-local final-token seal persisted atomically with the immutable receipt.</summary>
public sealed class OutboxTokenSealV2
{
    public Guid NativeId { get; set; }
    public string WriterTransaction { get; set; } = "";
}
public sealed class OutboxSendAttemptV2
{
    public string RootId { get; set; } = "";
    public Guid ClaimId { get; set; }
    public Guid MutationId { get; set; }
    public string OriginalTicket { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public string WriterTransaction { get; set; } = "";
}
public static class OutboxModelV2
{
    internal static void Validate(IModel model, string schema)
    {
        foreach (var (type, table) in new[]
        {
            (typeof(Cp6OutboxMessage), "Cp6_OutboxMessage"), (typeof(Cp6DeadLetterRecord), "Cp6_DeadLetterRecord"),
            (typeof(OutboxRouteControlV2), "Cp6_OutboxRouteV2"), (typeof(OutboxLeaseBindingV2), "Cp6_OutboxBindingV2"),
            (typeof(OutboxClaimReceiptV2), "Cp6_OutboxClaimReceiptV2"), (typeof(OutboxClaimTicketReceiptV2), "Cp6_OutboxClaimTicketV2"),
            (typeof(OutboxLeaseMutationReceiptV2), "Cp6_OutboxMutationReceiptV2"), (typeof(OutboxAuditV2), "Cp6_OutboxAuditV2"),
            (typeof(OutboxTokenSealV2), "Cp6_OutboxTokenSealV2"), (typeof(OutboxSendAttemptV2), "Cp6_OutboxSendAttemptV2")
        })
        {
            var entity = model.FindEntityType(type);
            if (entity?.GetSchema() != schema || entity.GetTableName() != table)
                throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
            foreach (var required in type.GetProperties())
                if (entity.FindProperty(required.Name) is null)
                    throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
            foreach (var property in entity.GetProperties())
            {
                var token = property.Name is "Version" or "RowVersion";
                if (property.GetColumnName(StoreObjectIdentifier.Table(table, schema)) != (token ? "xmin" : property.Name))
                    throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
                if (!token) continue;
                if (property.GetColumnType() != "xid" || !property.IsConcurrencyToken || property.ValueGenerated != ValueGenerated.OnAddOrUpdate)
                    throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
                var converter = property.GetValueConverter();
                if (property.ClrType == typeof(uint))
                {
                    if (converter is not null) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
                }
                else if (property.ClrType != typeof(byte[]) || converter?.ProviderClrType != typeof(uint) ||
                    !KnownCall(converter.ConvertToProviderExpression, nameof(OutboxEncodingV2.NativeValue)) ||
                    !KnownCall(converter.ConvertFromProviderExpression, nameof(OutboxEncodingV2.NativeBytes)))
                    throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
            }
        }
    }
    private static bool KnownCall(LambdaExpression expression, string method) => expression.Body is MethodCallExpression call &&
        call.Method.DeclaringType == typeof(OutboxEncodingV2) && call.Method.Name == method && call.Arguments.Count == 1 && call.Arguments[0] == expression.Parameters[0];

    /// <summary>Explicit PG opt-in, never called by the legacy model builder. Owner must separately migrate and install guards.</summary>
    public static ModelBuilder AddPostgreSqlOutboxV2(this ModelBuilder m, string schema)
    {
        OutboxEncodingV2.Text(schema);
        if (System.Text.Encoding.UTF8.GetByteCount(schema) > 63) throw new ArgumentException("PostgreSQL identifier exceeds 63 bytes.", nameof(schema));
        m.AddCp6TransactionalMessaging(schema);
        m.Entity<Cp6OutboxMessage>().Property(x => x.RowVersion).HasConversion(x => OutboxEncodingV2.NativeValue(x), x => OutboxEncodingV2.NativeBytes(x)).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        m.Entity<Cp6DeadLetterRecord>().Property(x => x.RowVersion).HasConversion(x => OutboxEncodingV2.NativeValue(x), x => OutboxEncodingV2.NativeBytes(x)).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        var r = m.Entity<OutboxRouteControlV2>(); r.ToTable("Cp6_OutboxRouteV2", schema); r.HasKey(x => x.Id);
        r.HasIndex(x => new { x.OwnerId, x.TenantId, x.QueueDomainId, x.RouteId }).IsUnique(); r.HasIndex(x => x.LockKey).IsUnique();
        r.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        var b = m.Entity<OutboxLeaseBindingV2>(); b.ToTable("Cp6_OutboxBindingV2", schema); b.HasKey(x => x.NativeId);
        b.HasIndex(x => x.MessageId).IsUnique(); b.HasIndex(x => new { x.RootId, x.AssignedEpoch });
        b.HasOne<OutboxRouteControlV2>().WithMany().HasForeignKey(x => x.RootId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Cp6OutboxMessage>().WithOne().HasForeignKey<OutboxLeaseBindingV2>(x => x.NativeId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        var c = m.Entity<OutboxClaimReceiptV2>(); c.ToTable("Cp6_OutboxClaimReceiptV2", schema); c.HasKey(x => new { x.RootId, x.RequestId });
        var i = m.Entity<OutboxClaimTicketReceiptV2>(); i.ToTable("Cp6_OutboxClaimTicketV2", schema); i.HasKey(x => new { x.RootId, x.RequestId, x.NativeId });
        i.HasIndex(x => x.ReceiptReference).IsUnique();
        i.HasOne<OutboxClaimReceiptV2>().WithMany().HasForeignKey(x => new { x.RootId, x.RequestId }).OnDelete(DeleteBehavior.Restrict);
        var u = m.Entity<OutboxLeaseMutationReceiptV2>(); u.ToTable("Cp6_OutboxMutationReceiptV2", schema); u.HasKey(x => new { x.RootId, x.MutationId });
        var a = m.Entity<OutboxAuditV2>(); a.ToTable("Cp6_OutboxAuditV2", schema); a.HasKey(x => x.Id);
        var s = m.Entity<OutboxTokenSealV2>(); s.ToTable("Cp6_OutboxTokenSealV2", schema); s.HasKey(x => new { x.NativeId, x.WriterTransaction });
        var p = m.Entity<OutboxSendAttemptV2>(); p.ToTable("Cp6_OutboxSendAttemptV2", schema); p.HasKey(x => new { x.RootId, x.ClaimId });
        p.HasIndex(x => new { x.RootId, x.MutationId }).IsUnique();
        return m;
    }
}
