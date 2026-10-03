using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.OutboxMessageId);
        builder.Property(o => o.OutboxMessageId).HasColumnName("outbox_message_id");
        builder.Property(o => o.CompanyId).HasColumnName("company_id");
        builder.Property(o => o.Topic).HasColumnName("topic").HasMaxLength(200).IsRequired();
        builder.Property(o => o.MessageKey).HasColumnName("message_key").HasMaxLength(200).IsRequired();
        builder.Property(o => o.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(o => o.Payload).HasColumnName("payload").IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.ProcessedAt).HasColumnName("processed_at");
        builder.Property(o => o.LeaseId).HasColumnName("lease_id").HasMaxLength(100);
        builder.Property(o => o.LeaseExpiresAt).HasColumnName("lease_expires_at");

        builder.HasIndex(o => new { o.ProcessedAt, o.CreatedAt })
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}
