namespace Sellora.DeliveryService.Domain.Entities;

public class OutboxMessage
{
    public Guid OutboxMessageId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Topic { get; private set; } = default!;
    public string MessageKey { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    private OutboxMessage() { }

    public OutboxMessage(
        Guid companyId,
        string topic,
        string messageKey,
        string eventType,
        string payload)
    {
        OutboxMessageId = Guid.NewGuid();
        CompanyId = companyId;
        Topic = topic;
        MessageKey = messageKey;
        EventType = eventType;
        Payload = payload;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;

    public void AcquireLease(string leaseId, TimeSpan duration)
    {
        LeaseId = leaseId;
        LeaseExpiresAt = DateTimeOffset.UtcNow.Add(duration);
    }
}
