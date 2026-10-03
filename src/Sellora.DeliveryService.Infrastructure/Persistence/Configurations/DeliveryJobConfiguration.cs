using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class DeliveryJobConfiguration : IEntityTypeConfiguration<DeliveryJob>
{
    public void Configure(EntityTypeBuilder<DeliveryJob> builder)
    {
        builder.ToTable("delivery_jobs");

        builder.HasKey(j => j.DeliveryJobId);
        builder.Property(j => j.DeliveryJobId).HasColumnName("delivery_job_id");
        builder.Property(j => j.CompanyId).HasColumnName("company_id");
        builder.Property(j => j.DeliveryReference).HasColumnName("delivery_reference").HasMaxLength(20).IsRequired();
        builder.Property(j => j.OrderId).HasColumnName("order_id");
        builder.Property(j => j.OrderReference).HasColumnName("order_reference").HasMaxLength(30).IsRequired();
        builder.Property(j => j.FulfilmentType).HasColumnName("fulfilment_type").HasMaxLength(50).IsRequired();

        builder.Property(j => j.ShopId).HasColumnName("shop_id");
        builder.Property(j => j.ShopName).HasColumnName("shop_name").HasMaxLength(200).IsRequired();
        builder.Property(j => j.ShopOwnerName).HasColumnName("shop_owner_name").HasMaxLength(200).IsRequired();
        builder.Property(j => j.ShopOwnerEmail).HasColumnName("shop_owner_email").HasMaxLength(320).IsRequired();

        builder.Property(j => j.AgencyId).HasColumnName("agency_id");
        builder.Property(j => j.AgencyName).HasColumnName("agency_name").HasMaxLength(200).IsRequired();
        builder.Property(j => j.AgencyEmail).HasColumnName("agency_email").HasMaxLength(320).IsRequired();

        builder.Property(j => j.TerritoryId).HasColumnName("territory_id");
        builder.Property(j => j.ProvinceId).HasColumnName("province_id");
        builder.Property(j => j.OrderSalesRepId).HasColumnName("order_sales_rep_id");

        builder.Property(j => j.Total).HasColumnName("total").HasColumnType("numeric(18,4)");
        builder.Property(j => j.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(j => j.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.AssignedRepId).HasColumnName("assigned_rep_id");
        builder.Property(j => j.AssignedRepName).HasColumnName("assigned_rep_name").HasMaxLength(200);
        builder.Property(j => j.ScheduledDate).HasColumnName("scheduled_date");
        builder.Property(j => j.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at");

        // xmin is PostgreSQL's built-in row version used as an optimistic concurrency token
        builder.Property(j => j.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .IsRequired();
        builder.Property(j => j.Version).ValueGeneratedOnAddOrUpdate();

        // Second idempotency guard: only one job per order per company
        builder.HasIndex(j => new { j.CompanyId, j.OrderId }).IsUnique().HasDatabaseName("ix_delivery_jobs_company_order");
        builder.HasIndex(j => j.DeliveryReference).HasDatabaseName("ix_delivery_jobs_delivery_reference");

        builder.HasMany(j => j.Lines)
            .WithOne()
            .HasForeignKey(l => l.DeliveryJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(j => j.StatusHistory)
            .WithOne()
            .HasForeignKey(h => h.DeliveryJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(j => j.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(j => j.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
