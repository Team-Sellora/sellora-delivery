namespace Sellora.DeliveryService.Application.Events;

public record DeliveryStatusChangedEvent(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid DeliveryId,
    Guid OrderId,
    string OrderReference,
    string? PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    string? Reason,
    DateOnly? ScheduledFor,
    DeliveryStatusChangedEvent.ShopInfo Shop,
    DeliveryStatusChangedEvent.AgencyInfo Agency,
    string DeliveryReference,
    Guid TerritoryId,
    Guid ProvinceId,
    DeliveryStatusChangedEvent.ActorInfo Actor)
{
    public record ShopInfo(Guid ShopId, string Name, string OwnerName, string OwnerEmail);
    public record AgencyInfo(Guid AgencyId, string Name, string Email);
    public record ActorInfo(string UserId, string Role);

    public static DeliveryStatusChangedEvent Create(
        Guid companyId,
        Guid deliveryId,
        Guid orderId,
        string orderReference,
        string? previousStatus,
        string status,
        DateTimeOffset occurredAt,
        string? reason,
        DateOnly? scheduledFor,
        Guid shopId,
        string shopName,
        string shopOwnerName,
        string shopOwnerEmail,
        Guid agencyId,
        string agencyName,
        string agencyEmail,
        string deliveryReference,
        Guid territoryId,
        Guid provinceId,
        string actorUserId,
        string actorRole,
        string? correlationId = null)
        => new(
            EventId: Guid.NewGuid(),
            EventType: "DeliveryStatusChanged",
            SchemaVersion: "1.0",
            CompanyId: companyId,
            DeliveryId: deliveryId,
            OrderId: orderId,
            OrderReference: orderReference,
            PreviousStatus: previousStatus,
            Status: status,
            OccurredAt: occurredAt,
            CorrelationId: correlationId,
            Reason: reason,
            ScheduledFor: scheduledFor,
            Shop: new ShopInfo(shopId, shopName, shopOwnerName, shopOwnerEmail),
            Agency: new AgencyInfo(agencyId, agencyName, agencyEmail),
            DeliveryReference: deliveryReference,
            TerritoryId: territoryId,
            ProvinceId: provinceId,
            Actor: new ActorInfo(actorUserId, actorRole));
}
