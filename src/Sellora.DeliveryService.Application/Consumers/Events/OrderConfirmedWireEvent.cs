using System.Text.Json;

namespace Sellora.DeliveryService.Application.Consumers.Events;

// Matches sellora-order's documented sellora.order.v1 JSON contract.
public sealed record OrderConfirmedWireEvent
{
    public Guid EventId { get; init; }
    public string? EventType { get; init; }
    public string? SchemaVersion { get; init; }
    public Guid CompanyId { get; init; }
    public Guid OrderId { get; init; }
    public string? OrderReference { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? FulfilmentType { get; init; }
    public EventShop? Shop { get; init; }
    public EventAgency? Agency { get; init; }
    public Guid TerritoryId { get; init; }
    public Guid ProvinceId { get; init; }
    public EventSalesRep? SalesRep { get; init; }
    public decimal Total { get; init; }
    public string? Currency { get; init; }
    public IReadOnlyList<OrderLineDto>? Lines { get; init; }
    public EventLocation? CheckoutLocation { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }

    public static OrderConfirmedEvent Parse(string json)
    {
        var wire = JsonSerializer.Deserialize<OrderConfirmedWireEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("OrderConfirmed event is null.");

        if (wire.EventType != "OrderConfirmed" || wire.SchemaVersion != "1.0" ||
            wire.EventId == Guid.Empty || wire.CompanyId == Guid.Empty || wire.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(wire.OrderReference) || string.IsNullOrWhiteSpace(wire.FulfilmentType) ||
            wire.Shop is null || wire.Shop.ShopId == Guid.Empty ||
            wire.Agency is null || wire.Agency.AgencyId == Guid.Empty ||
            wire.SalesRep is null || wire.SalesRep.SalesRepId == Guid.Empty ||
            wire.Lines is null || wire.Lines.Count == 0 ||
            wire.OccurredAt == default || wire.ConfirmedAt == default)
        {
            throw new JsonException("OrderConfirmed event is missing required v1 delivery fields.");
        }

        return new OrderConfirmedEvent(
            wire.EventId, wire.EventType, wire.CompanyId, wire.OrderId, wire.OrderReference,
            wire.OccurredAt, wire.FulfilmentType,
            wire.Shop.ShopId, wire.Shop.Name ?? string.Empty,
            wire.Shop.OwnerName ?? string.Empty, wire.Shop.OwnerEmail ?? string.Empty,
            wire.Agency.AgencyId, wire.Agency.Name ?? string.Empty, wire.Agency.Email ?? string.Empty,
            wire.TerritoryId, wire.ProvinceId, wire.SalesRep.SalesRepId,
            wire.Total, wire.Currency ?? "LKR", wire.Lines,
            wire.CheckoutLocation is null ? null : new CheckoutLocationDto(
                Guid.Empty, string.Empty, wire.CheckoutLocation.CheckedInAt),
            wire.ConfirmedAt);
    }
}

public sealed record EventShop(Guid ShopId, string? Name, string? OwnerName, string? OwnerEmail);
public sealed record EventAgency(Guid AgencyId, string? Name, string? Email);
public sealed record EventSalesRep(Guid SalesRepId, string? Name);
public sealed record EventLocation(DateTimeOffset CheckedInAt);
