namespace Sellora.DeliveryService.Application.Consumers.Events;

public record OrderEventBase(
    Guid EventId,
    string EventType,
    Guid CompanyId,
    Guid OrderId,
    string OrderReference,
    DateTimeOffset OccurredAt);

public record OrderConfirmedEvent(
    Guid EventId,
    string EventType,
    Guid CompanyId,
    Guid OrderId,
    string OrderReference,
    DateTimeOffset OccurredAt,
    string FulfilmentType,
    Guid ShopId,
    string ShopName,
    string ShopOwnerName,
    string ShopOwnerEmail,
    Guid AgencyId,
    string AgencyName,
    string AgencyEmail,
    Guid TerritoryId,
    Guid ProvinceId,
    Guid OrderSalesRepId,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineDto> Lines,
    CheckoutLocationDto? CheckoutLocation,
    DateTimeOffset ConfirmedAt) : OrderEventBase(EventId, EventType, CompanyId, OrderId, OrderReference, OccurredAt);

public record OrderLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public record CheckoutLocationDto(
    Guid LocationId,
    string LocationName,
    DateTimeOffset? CheckedInAt);

public record OrderCancelledEvent(
    Guid EventId,
    string EventType,
    Guid CompanyId,
    Guid OrderId,
    string OrderReference,
    DateTimeOffset OccurredAt,
    string? Reason,
    DateTimeOffset CancelledAt) : OrderEventBase(EventId, EventType, CompanyId, OrderId, OrderReference, OccurredAt);
