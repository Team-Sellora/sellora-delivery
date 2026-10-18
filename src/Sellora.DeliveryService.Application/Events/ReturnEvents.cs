using System.Diagnostics.CodeAnalysis;

namespace Sellora.DeliveryService.Application.Events;

public record ReturnShopInfo(Guid ShopId, string Name, string OwnerName, string OwnerEmail);
public record ReturnAgencyInfo(Guid AgencyId, string Name, string Email);

public record ReturnRequestedLine(Guid ProductId, string ProductName, int Quantity, string Reason);

public record ReturnRequestedEvent(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid EntityId,
    Guid ReturnRequestId,
    string ReturnReference,
    Guid DeliveryId,
    Guid OrderId,
    string OrderReference,
    ReturnShopInfo Shop,
    ReturnAgencyInfo Agency,
    Guid TerritoryId,
    Guid ProvinceId,
    DateTimeOffset RequestedAt,
    string RequestedByUserId,
    string RequestedByRole,
    IReadOnlyCollection<ReturnRequestedLine> Lines,
    string? CorrelationId = null
)
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Factory mirrors the published event contract field-for-field; changing its shape would change the wire format.")]
    public static ReturnRequestedEvent Create(
        Guid companyId,
        Guid returnRequestId,
        string returnReference,
        Guid deliveryId,
        Guid orderId,
        string orderReference,
        Guid shopId,
        string shopName,
        string shopOwnerName,
        string shopOwnerEmail,
        Guid agencyId,
        string agencyName,
        string agencyEmail,
        Guid territoryId,
        Guid provinceId,
        DateTimeOffset requestedAt,
        string requestedByUserId,
        string requestedByRole,
        IEnumerable<ReturnRequestedLine> lines,
        string? correlationId = null)
        => new(
            EventId: Guid.NewGuid(),
            EventType: "ReturnRequested",
            SchemaVersion: "1.0",
            CompanyId: companyId,
            EntityId: returnRequestId,
            ReturnRequestId: returnRequestId,
            ReturnReference: returnReference,
            DeliveryId: deliveryId,
            OrderId: orderId,
            OrderReference: orderReference,
            Shop: new ReturnShopInfo(shopId, shopName, shopOwnerName, shopOwnerEmail),
            Agency: new ReturnAgencyInfo(agencyId, agencyName, agencyEmail),
            TerritoryId: territoryId,
            ProvinceId: provinceId,
            RequestedAt: requestedAt,
            RequestedByUserId: requestedByUserId,
            RequestedByRole: requestedByRole,
            Lines: lines.ToList().AsReadOnly(),
            CorrelationId: correlationId);
}

public record ReturnAcceptedLine(Guid ProductId, Guid? BatchId, int Quantity, string? ProductName = null);

public record ReturnAcceptedEvent(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid EntityId,
    Guid InventoryOwnerId,
    string ReturnReference,
    IReadOnlyCollection<ReturnAcceptedLine> Lines,
    DateTimeOffset AcceptedAt,
    string CorrelationId,
    Guid DeliveryId,
    string OrderReference,
    ReturnShopInfo Shop,
    ReturnAgencyInfo Agency
)
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Factory mirrors the published event contract field-for-field; changing its shape would change the wire format.")]
    public static ReturnAcceptedEvent Create(
        Guid companyId,
        Guid returnRequestId,
        Guid inventoryOwnerId,
        string returnReference,
        DateTimeOffset acceptedAt,
        string correlationId,
        Guid deliveryId,
        string orderReference,
        Guid shopId,
        string shopName,
        string shopOwnerName,
        string shopOwnerEmail,
        Guid agencyId,
        string agencyName,
        string agencyEmail,
        IEnumerable<ReturnAcceptedLine> lines)
        => new(
            EventId: Guid.NewGuid(),
            EventType: "ReturnAccepted",
            SchemaVersion: "1.0",
            CompanyId: companyId,
            EntityId: returnRequestId,
            InventoryOwnerId: inventoryOwnerId,
            ReturnReference: returnReference,
            Lines: lines.ToList().AsReadOnly(),
            AcceptedAt: acceptedAt,
            CorrelationId: correlationId,
            DeliveryId: deliveryId,
            OrderReference: orderReference,
            Shop: new ReturnShopInfo(shopId, shopName, shopOwnerName, shopOwnerEmail),
            Agency: new ReturnAgencyInfo(agencyId, agencyName, agencyEmail));
}

public record ReturnRejectedEvent(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid EntityId,
    Guid ReturnRequestId,
    string ReturnReference,
    Guid DeliveryId,
    Guid OrderId,
    string OrderReference,
    ReturnShopInfo Shop,
    ReturnAgencyInfo Agency,
    DateTimeOffset RejectedAt,
    string RejectedByUserId,
    string RejectedByRole,
    string Reason,
    string? CorrelationId = null
)
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Factory mirrors the published event contract field-for-field; changing its shape would change the wire format.")]
    public static ReturnRejectedEvent Create(
        Guid companyId,
        Guid returnRequestId,
        string returnReference,
        Guid deliveryId,
        Guid orderId,
        string orderReference,
        Guid shopId,
        string shopName,
        string shopOwnerName,
        string shopOwnerEmail,
        Guid agencyId,
        string agencyName,
        string agencyEmail,
        DateTimeOffset rejectedAt,
        string rejectedByUserId,
        string rejectedByRole,
        string reason,
        string? correlationId = null)
        => new(
            EventId: Guid.NewGuid(),
            EventType: "ReturnRejected",
            SchemaVersion: "1.0",
            CompanyId: companyId,
            EntityId: returnRequestId,
            ReturnRequestId: returnRequestId,
            ReturnReference: returnReference,
            DeliveryId: deliveryId,
            OrderId: orderId,
            OrderReference: orderReference,
            Shop: new ReturnShopInfo(shopId, shopName, shopOwnerName, shopOwnerEmail),
            Agency: new ReturnAgencyInfo(agencyId, agencyName, agencyEmail),
            RejectedAt: rejectedAt,
            RejectedByUserId: rejectedByUserId,
            RejectedByRole: rejectedByRole,
            Reason: reason,
            CorrelationId: correlationId);
}
