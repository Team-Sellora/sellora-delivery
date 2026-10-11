using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Infrastructure.Persistence.Configurations;

public class DeliveryConfirmationConfiguration : IEntityTypeConfiguration<DeliveryConfirmation>
{
    public void Configure(EntityTypeBuilder<DeliveryConfirmation> builder)
    {
        builder.ToTable("DeliveryConfirmations");

        builder.HasKey(c => c.DeliveryConfirmationId);

        // Enforce 1:1 and race-condition resilience
        builder.HasIndex(c => c.DeliveryJobId).IsUnique();

        builder.Property(c => c.Type)
            .HasConversion(
                v => v.ToString(),
                v => (ConfirmationType)Enum.Parse(typeof(ConfirmationType), v))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.Notes)
            .HasMaxLength(1000);

        builder.Property(c => c.SignatureUrl)
            .HasMaxLength(2000);

        // Optimistic concurrency is probably not needed since it's insert-only,
        // but we can ensure standard DB types if needed.
    }
}
