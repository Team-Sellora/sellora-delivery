using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests");

        builder.HasKey(r => r.ReturnRequestId);
        builder.Property(r => r.ReturnRequestId).HasColumnName("return_request_id");
        builder.Property(r => r.CompanyId).HasColumnName("company_id");
        builder.Property(r => r.ReturnReference).HasColumnName("return_reference").HasMaxLength(20).IsRequired();
        builder.Property(r => r.DeliveryJobId).HasColumnName("delivery_job_id");
        builder.Property(r => r.OrderId).HasColumnName("order_id");
        builder.Property(r => r.OrderReference).HasColumnName("order_reference").HasMaxLength(30).IsRequired();

        builder.Property(r => r.ShopId).HasColumnName("shop_id");
        builder.Property(r => r.AgencyId).HasColumnName("agency_id");
        builder.Property(r => r.TerritoryId).HasColumnName("territory_id");
        builder.Property(r => r.ProvinceId).HasColumnName("province_id");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.RequestedByUserId).HasColumnName("requested_by_user_id").HasMaxLength(200).IsRequired();
        builder.Property(r => r.RequestedByRole).HasColumnName("requested_by_role").HasMaxLength(50).IsRequired();
        builder.Property(r => r.RequestedAt).HasColumnName("requested_at");

        builder.Property(r => r.DecidedByUserId).HasColumnName("decided_by_user_id").HasMaxLength(200);
        builder.Property(r => r.DecidedByRole).HasColumnName("decided_by_role").HasMaxLength(50);
        builder.Property(r => r.DecidedAt).HasColumnName("decided_at");
        builder.Property(r => r.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);
        builder.Property(r => r.InventoryOwnerId).HasColumnName("inventory_owner_id");

        builder.Property(r => r.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .IsRequired();
        builder.Property(r => r.Version).ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(r => r.ReturnReference).IsUnique().HasDatabaseName("ix_return_requests_reference");
        builder.HasIndex(r => new { r.CompanyId, r.DeliveryJobId }).HasDatabaseName("ix_return_requests_company_delivery_job");
        builder.HasIndex(r => new { r.CompanyId, r.AgencyId, r.Status }).HasDatabaseName("ix_return_requests_agency_status");

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.ReturnRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
