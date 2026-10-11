using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

internal class ReturnRequestLineConfiguration : IEntityTypeConfiguration<ReturnRequestLine>
{
    public void Configure(EntityTypeBuilder<ReturnRequestLine> builder)
    {
        builder.ToTable("return_request_lines");

        builder.HasKey(l => l.ReturnRequestLineId);
        builder.Property(l => l.ReturnRequestLineId).HasColumnName("return_request_line_id");
        builder.Property(l => l.ReturnRequestId).HasColumnName("return_request_id");
        builder.Property(l => l.CompanyId).HasColumnName("company_id");
        builder.Property(l => l.ProductId).HasColumnName("product_id");
        builder.Property(l => l.ProductName).HasColumnName("product_name").HasMaxLength(300).IsRequired();
        builder.Property(l => l.Quantity).HasColumnName("quantity");
        builder.Property(l => l.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();

        builder.HasIndex(l => l.ReturnRequestId).HasDatabaseName("ix_return_request_lines_return_id");
        builder.HasIndex(l => l.ProductId).HasDatabaseName("ix_return_request_lines_product_id");
    }
}
