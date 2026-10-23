using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class ProcessedDeliveryEventConfiguration : IEntityTypeConfiguration<ProcessedDeliveryEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedDeliveryEvent> builder)
    {
        builder.ToTable("processed_delivery_events");

        builder.HasKey(e => new { e.CompanyId, e.EventId });
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.EventId).HasColumnName("event_id");
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
    }
}
