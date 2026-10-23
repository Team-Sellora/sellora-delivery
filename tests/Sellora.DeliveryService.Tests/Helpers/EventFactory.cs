using Sellora.DeliveryService.Application.Consumers.Events;

namespace Sellora.DeliveryService.Tests.Helpers;

internal static class EventFactory
{
    public static OrderConfirmedEvent BuildOrderConfirmedEvent(
        string fulfilmentType = "ScheduledDelivery",
        CheckoutLocationDto? checkoutLocation = null)
    {
        return new OrderConfirmedEvent(
            EventId: Guid.NewGuid(),
            EventType: "OrderConfirmed",
            CompanyId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            OrderReference: $"ORD-{DateTimeOffset.UtcNow:yyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
            OccurredAt: DateTimeOffset.UtcNow,
            FulfilmentType: fulfilmentType,
            ShopId: Guid.NewGuid(),
            ShopName: "Sunshine Shop",
            ShopOwnerName: "Alice Smith",
            ShopOwnerEmail: "alice@sunshine.com",
            AgencyId: Guid.NewGuid(),
            AgencyName: "Central Agency",
            AgencyEmail: "central@agency.com",
            TerritoryId: Guid.NewGuid(),
            ProvinceId: Guid.NewGuid(),
            OrderSalesRepId: Guid.NewGuid(),
            Total: 2500.00m,
            Currency: "MWK",
            Lines: new List<OrderLineDto>
            {
                new(Guid.NewGuid(), "Sugar 2kg", 20, 75m, 1500m),
                new(Guid.NewGuid(), "Salt 1kg", 10, 100m, 1000m)
            },
            CheckoutLocation: checkoutLocation,
            ConfirmedAt: DateTimeOffset.UtcNow.AddMinutes(-3));
    }
}
