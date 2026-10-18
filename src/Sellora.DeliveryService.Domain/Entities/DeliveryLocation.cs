using System.Diagnostics.CodeAnalysis;

namespace Sellora.DeliveryService.Domain.Entities;

public class DeliveryLocation
{
    public Guid DeliveryLocationId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DeliveryJobId { get; private set; }
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string RecordedByUserId { get; private set; } = default!;

    private DeliveryLocation() { }

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Domain factory takes every field of the entity snapshot; callers use named arguments.")]
    public static DeliveryLocation Create(
        Guid companyId,
        Guid deliveryJobId,
        decimal latitude,
        decimal longitude,
        double? accuracyMeters,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        string recordedByUserId)
    {
        return new DeliveryLocation
        {
            DeliveryLocationId = Guid.NewGuid(),
            CompanyId = companyId,
            DeliveryJobId = deliveryJobId,
            Latitude = latitude,
            Longitude = longitude,
            AccuracyMeters = accuracyMeters,
            CapturedAt = capturedAt.ToUniversalTime(),
            ReceivedAt = receivedAt.ToUniversalTime(),
            RecordedByUserId = recordedByUserId
        };
    }
}
