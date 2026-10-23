using System.Diagnostics.CodeAnalysis;

namespace Sellora.DeliveryService.Application.Events;

public record DeliveryJobCreatedEvent(
    Guid EventId,
    string EventType,
    Guid CompanyId,
    Guid DeliveryJobId,
    string DeliveryReference,
    Guid OrderId,
    string OrderReference,
    string FulfilmentType,
    Guid AgencyId,
    string AgencyName,
    Guid ShopId,
    string ShopName,
    string Status,
    decimal Total,
    string Currency,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset CreatedAt)
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Factory mirrors the published event contract field-for-field; changing its shape would change the wire format.")]
    public static DeliveryJobCreatedEvent Create(
        Guid companyId,
        Guid deliveryJobId,
        string deliveryReference,
        Guid orderId,
        string orderReference,
        string fulfilmentType,
        Guid agencyId,
        string agencyName,
        Guid shopId,
        string shopName,
        string status,
        decimal total,
        string currency,
        DateTimeOffset? deliveredAt,
        DateTimeOffset createdAt)
        => new(
            EventId: Guid.NewGuid(),
            EventType: "DeliveryJobCreated",
            CompanyId: companyId,
            DeliveryJobId: deliveryJobId,
            DeliveryReference: deliveryReference,
            OrderId: orderId,
            OrderReference: orderReference,
            FulfilmentType: fulfilmentType,
            AgencyId: agencyId,
            AgencyName: agencyName,
            ShopId: shopId,
            ShopName: shopName,
            Status: status,
            Total: total,
            Currency: currency,
            DeliveredAt: deliveredAt,
            CreatedAt: createdAt);
}
