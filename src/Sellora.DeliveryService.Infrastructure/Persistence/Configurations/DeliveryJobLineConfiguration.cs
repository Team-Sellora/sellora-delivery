using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class DeliveryJobLineConfiguration : IEntityTypeConfiguration<DeliveryJobLine>
{
    public void Configure(EntityTypeBuilder<DeliveryJobLine> builder)
    {
        builder.ToTable("delivery_job_lines");

        builder.HasKey(l => l.DeliveryJobLineId);
        builder.Property(l => l.DeliveryJobLineId).HasColumnName("delivery_job_line_id");
        builder.Property(l => l.DeliveryJobId).HasColumnName("delivery_job_id");
        builder.Property(l => l.CompanyId).HasColumnName("company_id");
        builder.Property(l => l.ProductId).HasColumnName("product_id");
        builder.Property(l => l.ProductName).HasColumnName("product_name").HasMaxLength(300).IsRequired();
        builder.Property(l => l.Quantity).HasColumnName("quantity");
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,4)");
        builder.Property(l => l.LineTotal).HasColumnName("line_total").HasColumnType("numeric(18,4)");

        builder.HasIndex(l => l.DeliveryJobId).HasDatabaseName("ix_delivery_job_lines_job_id");
    }
}
