using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework.InboxV1;

/// <summary>Immutable original transport binding. Contains metadata/hashes only, not original inbound payload.</summary>
public sealed class InboxTransportV1
{
    public Guid Id { get; internal set; }
    public string Consumer { get; internal set; } = "";
    public string MessageId { get; internal set; } = "";
    public Guid TenantId { get; internal set; }
    public string Topic { get; internal set; } = "";
    public string Partition { get; internal set; } = "";
    public string Aggregate { get; internal set; } = "";
    public int Version { get; internal set; }
    public string RawSha256 { get; internal set; } = "";
    public string DataSha256 { get; internal set; } = "";
    public string EventType { get; internal set; } = "";
    public string Source { get; internal set; } = "";
    public string Region { get; internal set; } = "";
    public string SchemaIdentity { get; internal set; } = "";
    public string BundleIdentity { get; internal set; } = "";
    public string OperationBindingSha256 { get; internal set; } = "";
    public uint RowVersion { get; internal set; }

    internal static InboxTransportV1 From(InboxInputV1 input) => new()
    {
        Id = Guid.NewGuid(), Consumer = input.Delivery.ConsumerName, MessageId = input.Delivery.MessageId,
        TenantId = input.Delivery.TenantId, Topic = input.Delivery.TopicName, Partition = input.Delivery.PartitionKey,
        Aggregate = input.Delivery.AggregateId, Version = input.Delivery.AggregateVersion,
        RawSha256 = input.RawEnvelopeSha256, DataSha256 = input.DataPayloadSha256,
        EventType = input.Binding.EventType, Source = input.Binding.Source, Region = input.Binding.Region,
        SchemaIdentity = input.Binding.SchemaIdentity, BundleIdentity = input.Binding.BundleIdentity,
        OperationBindingSha256 = OperationBinding(input.Binding)
    };
    internal bool Matches(InboxInputV1 input) => TenantId == input.Delivery.TenantId &&
        Consumer == input.Delivery.ConsumerName && MessageId == input.Delivery.MessageId &&
        Topic == input.Delivery.TopicName && Partition == input.Delivery.PartitionKey &&
        Aggregate == input.Delivery.AggregateId && Version == input.Delivery.AggregateVersion &&
        RawSha256 == input.RawEnvelopeSha256 && DataSha256 == input.DataPayloadSha256 &&
        EventType == input.Binding.EventType && Source == input.Binding.Source && Region == input.Binding.Region &&
        SchemaIdentity == input.Binding.SchemaIdentity && BundleIdentity == input.Binding.BundleIdentity &&
        OperationBindingSha256 == OperationBinding(input.Binding);
    private static string OperationBinding(InboxContractBindingV1 b) => InboxInputV1.Hash(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] { b.Owner, b.OperationKey, b.RequestVersion, b.InputDomain, b.InputCodec, b.InputDigest }));
}

public sealed class InboxCheckpointV1
{
    public Guid Id { get; internal set; }
    public string Consumer { get; internal set; } = "";
    public Guid TenantId { get; internal set; }
    public string Aggregate { get; internal set; } = "";
    public int Version { get; internal set; }
    public uint RowVersion { get; internal set; }
}

/// <summary>Immutable copy of an Owner's ORIGINAL journal result; never a substitute business authority.</summary>
public sealed class InboxOriginalResultV1
{
    public Guid Id { get; internal set; }
    public Guid TenantId { get; internal set; }
    public string Owner { get; internal set; } = "";
    public string OperationKey { get; internal set; } = "";
    public string RequestVersion { get; internal set; } = "";
    public string InputDomain { get; internal set; } = "";
    public string InputCodec { get; internal set; } = "";
    public string InputDigest { get; internal set; } = "";
    public string NativeReceiptReference { get; internal set; } = "";
    public byte[] Result { get; internal set; } = [];
    public string ResultSha256 { get; internal set; } = "";
    public uint RowVersion { get; internal set; }
}

/// <summary>Append-only technical decision evidence. Conflicts do not create or overwrite original business results.</summary>
public sealed class InboxDeliveryOutcomeV1
{
    public Guid Id { get; internal set; }
    public Guid TenantId { get; internal set; }
    public string Owner { get; internal set; } = "";
    public string OperationKey { get; internal set; } = "";
    public string RequestVersion { get; internal set; } = "";
    public string InputDomain { get; internal set; } = "";
    public string InputCodec { get; internal set; } = "";
    public string InputDigest { get; internal set; } = "";
    public string Consumer { get; internal set; } = "";
    public string MessageId { get; internal set; } = "";
    public string RawSha256 { get; internal set; } = "";
    public string DataSha256 { get; internal set; } = "";
    public string NativeEvidenceReference { get; internal set; } = "";
    public string RegistrationReference { get; internal set; } = "";
    public string SourceReference { get; internal set; } = "";
    public int? SourceVersion { get; internal set; }
    public PreCheckpointIdentityDecisionV1 Decision { get; internal set; }
    public Guid? OriginalResultId { get; internal set; }
    public uint RowVersion { get; internal set; }
}

public static class InboxModelV1
{
    /// <summary>Owner opts into separate PostgreSQL tables. No automatic migration/retention or legacy model change.</summary>
    public static void AddCallerTransactionInboxV1(this ModelBuilder model, string schema)
    {
        ArgumentNullException.ThrowIfNull(model);
        InboxValidationV1.Text(schema);
        Configure<InboxTransportV1>(model, schema, "InboxV1Transport");
        Configure<InboxCheckpointV1>(model, schema, "InboxV1Checkpoint");
        Configure<InboxOriginalResultV1>(model, schema, "InboxV1OriginalResult");
        Configure<InboxDeliveryOutcomeV1>(model, schema, "InboxV1DeliveryOutcome");
        model.Entity<InboxTransportV1>().HasIndex(x => new { x.Consumer, x.MessageId }).IsUnique();
        model.Entity<InboxCheckpointV1>().HasIndex(x => new { x.Consumer, x.TenantId, x.Aggregate }).IsUnique();
        model.Entity<InboxOriginalResultV1>().HasIndex(x => new { x.TenantId, x.Owner, x.OperationKey, x.RequestVersion }).IsUnique();
        model.Entity<InboxDeliveryOutcomeV1>().HasOne<InboxOriginalResultV1>().WithMany()
            .HasForeignKey(x => x.OriginalResultId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void Configure<T>(ModelBuilder model, string schema, string table) where T : class
    {
        var entity = model.Entity<T>();
        entity.ToTable(table, schema);
        entity.HasKey("Id");
        // Npgsql maps a generated uint concurrency token to PostgreSQL's actual xmin system column.
        entity.Property<uint>("RowVersion").IsRowVersion();
    }
}
