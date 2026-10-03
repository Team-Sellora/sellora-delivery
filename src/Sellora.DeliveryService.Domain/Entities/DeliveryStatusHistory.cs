using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Domain.Entities;

// Append-only — never updated or deleted after insert.
public class DeliveryStatusHistory
{
    public Guid DeliveryStatusHistoryId { get; private set; }
    public Guid DeliveryJobId { get; private set; }
    public Guid CompanyId { get; private set; }
    public DeliveryStatus? FromStatus { get; private set; }
    public DeliveryStatus ToStatus { get; private set; }
    public string ActorUserId { get; private set; } = default!;
    public string ActorRole { get; private set; } = default!;
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private DeliveryStatusHistory() { }

    public DeliveryStatusHistory(
        Guid deliveryJobId,
        Guid companyId,
        DeliveryStatus? fromStatus,
        DeliveryStatus toStatus,
        string actorUserId,
        string actorRole,
        string? reason,
        DateTimeOffset occurredAt)
    {
        DeliveryStatusHistoryId = Guid.NewGuid();
        DeliveryJobId = deliveryJobId;
        CompanyId = companyId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        Reason = reason;
        OccurredAt = occurredAt;
    }
}
