using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Domain.Entities;

public class DeliveryConfirmation
{
    public Guid DeliveryConfirmationId { get; private set; }
    public Guid DeliveryJobId { get; private set; }
    public Guid CompanyId { get; private set; }
    public ConfirmationType Type { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public string? Notes { get; private set; }
    public string? SignatureUrl { get; private set; }

    private DeliveryConfirmation() { }

    public static DeliveryConfirmation Create(
        Guid deliveryJobId,
        Guid companyId,
        ConfirmationType type,
        string? notes,
        string? signatureUrl)
    {
        return new DeliveryConfirmation
        {
            DeliveryConfirmationId = Guid.NewGuid(),
            DeliveryJobId = deliveryJobId,
            CompanyId = companyId,
            Type = type,
            ConfirmedAt = DateTimeOffset.UtcNow,
            Notes = notes,
            SignatureUrl = signatureUrl
        };
    }
}
