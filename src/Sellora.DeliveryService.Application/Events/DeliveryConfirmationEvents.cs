namespace Sellora.DeliveryService.Application.Events;

public record DeliveryConfirmedEvent(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid DeliveryId,
    Guid OrderId,
    string OrderReference,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    string DeliveryReference,
    Guid TerritoryId,
    Guid ProvinceId,
    DeliveryConfirmedEvent.ShopInfo Shop,
    DeliveryConfirmedEvent.AgencyInfo Agency,
    DeliveryConfirmedEvent.ActorInfo Actor,
    string Outcome,
    string? Note,
    DateTimeOffset ConfirmedAt,
    string? SignatureUrl = null
)
{
    public record ShopInfo(Guid ShopId, string Name, string OwnerName, string OwnerEmail);
    public record AgencyInfo(Guid AgencyId, string Name, string Email);
    public record ActorInfo(string UserId, string Role);

    public static DeliveryConfirmedEvent Create(
        Guid companyId,
        Guid deliveryId,
        Guid orderId,
        string orderReference,
        DateTimeOffset confirmedAt,
        string? note,
        string? signatureUrl,
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
            EventType: "DeliveryConfirmed",
            SchemaVersion: "1.0",
            CompanyId: companyId,
            DeliveryId: deliveryId,
            OrderId: orderId,
            OrderReference: orderReference,
            OccurredAt: confirmedAt,
            CorrelationId: correlationId,
            DeliveryReference: deliveryReference,
            TerritoryId: territoryId,
            ProvinceId: provinceId,
            Shop: new ShopInfo(shopId, shopName, shopOwnerName, shopOwnerEmail),
            Agency: new AgencyInfo(agencyId, agencyName, agencyEmail),
            Actor: new ActorInfo(actorUserId, actorRole),
            Outcome: "Confirmed",
            Note: note,
            ConfirmedAt: confirmedAt,
            SignatureUrl: signatureUrl
        );
}

public record DeliveryDisputedEvent(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid DeliveryId,
    Guid OrderId,
    string OrderReference,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    string DeliveryReference,
    Guid TerritoryId,
    Guid ProvinceId,
    DeliveryDisputedEvent.ShopInfo Shop,
    DeliveryDisputedEvent.AgencyInfo Agency,
    DeliveryDisputedEvent.ActorInfo Actor,
    string Outcome,
    string? Note,
    string DisputeReason,
    string RaisedByRole,
    DateTimeOffset DisputedAt,
    string? SignatureUrl = null
)
{
    public record ShopInfo(Guid ShopId, string Name, string OwnerName, string OwnerEmail);
    public record AgencyInfo(Guid AgencyId, string Name, string Email);
    public record ActorInfo(string UserId, string Role);

    public static DeliveryDisputedEvent Create(
        Guid companyId,
        Guid deliveryId,
        Guid orderId,
        string orderReference,
        DateTimeOffset disputedAt,
        string? note,
        string? signatureUrl,
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
            EventType: "DeliveryDisputed",
            SchemaVersion: "1.0",
            CompanyId: companyId,
            DeliveryId: deliveryId,
            OrderId: orderId,
            OrderReference: orderReference,
            OccurredAt: disputedAt,
            CorrelationId: correlationId,
            DeliveryReference: deliveryReference,
            TerritoryId: territoryId,
            ProvinceId: provinceId,
            Shop: new ShopInfo(shopId, shopName, shopOwnerName, shopOwnerEmail),
            Agency: new AgencyInfo(agencyId, agencyName, agencyEmail),
            Actor: new ActorInfo(actorUserId, actorRole),
            Outcome: "Disputed",
            Note: note,
            DisputeReason: note ?? string.Empty,
            RaisedByRole: "ShopOwner",
            DisputedAt: disputedAt,
            SignatureUrl: signatureUrl
        );
}
