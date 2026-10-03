namespace Sellora.DeliveryService.Domain.Entities;

// Receipt table — one row per (company_id, event_id) pair to guard against duplicate processing.
public class ProcessedDeliveryEvent
{
    public Guid CompanyId { get; private set; }
    public Guid EventId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedDeliveryEvent() { }

    public ProcessedDeliveryEvent(Guid companyId, Guid eventId)
    {
        CompanyId = companyId;
        EventId = eventId;
        ProcessedAt = DateTimeOffset.UtcNow;
    }
}
