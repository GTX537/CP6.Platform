using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CP6.Platform.EntityFramework;

public static class Cp6TransactionalMessagingModelBuilderExtensions
{
    public static ModelBuilder AddCp6TransactionalMessaging(this ModelBuilder modelBuilder, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (schema is not null)
        {
            Cp6TransactionalMessagingGuard.Identifier(schema, nameof(schema), 128);
        }

        ConfigureOutbox(modelBuilder.Entity<Cp6OutboxMessage>(), schema);
        ConfigureInbox(modelBuilder.Entity<Cp6InboxMessage>(), schema);
        ConfigureCheckpoint(modelBuilder.Entity<Cp6InboxAggregateCheckpoint>(), schema);
        ConfigureDeadLetter(modelBuilder.Entity<Cp6DeadLetterRecord>(), schema);
        return modelBuilder;
    }

    private static void ConfigureOutbox(EntityTypeBuilder<Cp6OutboxMessage> entity, string? schema)
    {
        entity.ToTable("Cp6_OutboxMessage", schema);
        entity.HasKey(message => message.Id);
        entity.HasIndex(message => message.MessageId).IsUnique();
        entity.HasIndex(message => new { message.Status, message.AvailableAtUtc, message.LeaseExpiresAtUtc });
        entity.HasIndex(message => new { message.TenantId, message.AggregateId, message.AggregateVersion });
        entity.Property(message => message.MessageId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.TopicName).HasMaxLength(249).IsRequired();
        entity.Property(message => message.PartitionKey).HasMaxLength(512).IsRequired();
        entity.Property(message => message.Payload).IsRequired();
        entity.Property(message => message.PayloadSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(message => message.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.CausationId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.AggregateId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.LeaseOwner).HasMaxLength(128);
        entity.Property(message => message.LeaseToken).HasMaxLength(32).IsUnicode(false);
        entity.Property(message => message.LastErrorCode).HasMaxLength(128).IsUnicode(false);
        entity.Property(message => message.SupportReference).HasMaxLength(128);
        entity.Property(message => message.RowVersion).IsRowVersion();
    }

    private static void ConfigureInbox(EntityTypeBuilder<Cp6InboxMessage> entity, string? schema)
    {
        entity.ToTable("Cp6_InboxMessage", schema);
        entity.HasKey(message => message.Id);
        entity.HasIndex(message => new { message.ConsumerName, message.MessageId }).IsUnique();
        entity.HasIndex(message => new { message.Status, message.ProcessedAtUtc });
        entity.Property(message => message.ConsumerName).HasMaxLength(128).IsRequired();
        entity.Property(message => message.MessageId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.TopicName).HasMaxLength(249).IsRequired();
        entity.Property(message => message.PartitionKey).HasMaxLength(512).IsRequired();
        entity.Property(message => message.PayloadSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(message => message.AggregateId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.OutcomeCode).HasMaxLength(128).IsUnicode(false);
        entity.Property(message => message.LastErrorCode).HasMaxLength(128).IsUnicode(false);
        entity.Property(message => message.SupportReference).HasMaxLength(128);
        entity.Property(message => message.RowVersion).IsRowVersion();
    }

    private static void ConfigureCheckpoint(EntityTypeBuilder<Cp6InboxAggregateCheckpoint> entity, string? schema)
    {
        entity.ToTable("Cp6_InboxAggregateCheckpoint", schema);
        entity.HasKey(checkpoint => checkpoint.Id);
        entity.HasIndex(checkpoint => new { checkpoint.ConsumerName, checkpoint.TenantId, checkpoint.AggregateId }).IsUnique();
        entity.Property(checkpoint => checkpoint.ConsumerName).HasMaxLength(128).IsRequired();
        entity.Property(checkpoint => checkpoint.AggregateId).HasMaxLength(128).IsRequired();
        entity.Property(checkpoint => checkpoint.RowVersion).IsRowVersion();
    }

    private static void ConfigureDeadLetter(EntityTypeBuilder<Cp6DeadLetterRecord> entity, string? schema)
    {
        entity.ToTable("Cp6_DeadLetterRecord", schema);
        entity.HasKey(record => record.Id);
        entity.HasIndex(record => new { record.Direction, record.MessageId, record.ConsumerName });
        entity.HasIndex(record => record.CreatedAtUtc);
        entity.Property(record => record.MessageId).HasMaxLength(128).IsRequired();
        entity.Property(record => record.ConsumerName).HasMaxLength(128);
        entity.Property(record => record.PayloadSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(record => record.ErrorCode).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(record => record.SupportReference).HasMaxLength(128);
        entity.Property(record => record.ReplayReasonCode).HasMaxLength(128).IsUnicode(false);
        entity.Property(record => record.RowVersion).IsRowVersion();
    }
}
