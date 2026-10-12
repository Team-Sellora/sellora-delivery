using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class DeliveryLocationConfiguration : IEntityTypeConfiguration<DeliveryLocation>
{
    public void Configure(EntityTypeBuilder<DeliveryLocation> builder)
    {
        builder.ToTable("delivery_locations");

        builder.HasKey(l => l.DeliveryLocationId);
        builder.Property(l => l.DeliveryLocationId).HasColumnName("delivery_location_id");
        builder.Property(l => l.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(l => l.DeliveryJobId).HasColumnName("delivery_job_id").IsRequired();
        builder.Property(l => l.Latitude).HasColumnName("latitude").HasPrecision(9, 6).IsRequired();
        builder.Property(l => l.Longitude).HasColumnName("longitude").HasPrecision(9, 6).IsRequired();
        builder.Property(l => l.AccuracyMeters).HasColumnName("accuracy_meters");
        builder.Property(l => l.CapturedAt).HasColumnName("captured_at").IsRequired();
        builder.Property(l => l.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(l => l.RecordedByUserId).HasColumnName("recorded_by_user_id").HasMaxLength(200).IsRequired();

        builder.HasIndex(l => new { l.DeliveryJobId, l.CapturedAt })
            .HasDatabaseName("ix_delivery_locations_delivery_job_id_captured_at");

        builder.HasIndex(l => l.CompanyId)
            .HasDatabaseName("ix_delivery_locations_company_id");
    }
}
