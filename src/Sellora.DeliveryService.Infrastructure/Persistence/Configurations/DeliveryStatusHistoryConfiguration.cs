using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class DeliveryStatusHistoryConfiguration : IEntityTypeConfiguration<DeliveryStatusHistory>
{
    public void Configure(EntityTypeBuilder<DeliveryStatusHistory> builder)
    {
        builder.ToTable("delivery_status_history");

        builder.HasKey(h => h.DeliveryStatusHistoryId);
        builder.Property(h => h.DeliveryStatusHistoryId).HasColumnName("delivery_status_history_id");
        builder.Property(h => h.DeliveryJobId).HasColumnName("delivery_job_id");
        builder.Property(h => h.CompanyId).HasColumnName("company_id");
        builder.Property(h => h.FromStatus).HasColumnName("from_status").HasConversion<string?>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasColumnName("to_status").HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(100).IsRequired();
        builder.Property(h => h.ActorRole).HasColumnName("actor_role").HasMaxLength(50).IsRequired();
        builder.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(h => h.OccurredAt).HasColumnName("occurred_at");

        builder.HasIndex(h => h.DeliveryJobId).HasDatabaseName("ix_delivery_status_history_job_id");
    }
}
