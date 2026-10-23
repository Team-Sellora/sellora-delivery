using System.Text.Json;
using FluentAssertions;
using Sellora.DeliveryService.Application.Consumers.Events;

namespace Sellora.DeliveryService.Tests.Integration;

public class OrderContractTests
{
    internal static readonly Guid CompanyId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    internal static readonly Guid OrderId = Guid.Parse("8c1e3d52-6a1f-4b7e-9d2c-0f4e5a6b7c8d");

    // Snapshot of sellora-order's documented OrderConfirmed v1 payload shape.
    internal static string ConfirmedJson(Guid eventId) => $$"""
    {
      "eventId": "{{eventId}}",
      "eventType": "OrderConfirmed",
      "schemaVersion": "1.0",
      "companyId": "{{CompanyId}}",
      "entityId": "{{OrderId}}",
      "orderId": "{{OrderId}}",
      "orderReference": "ORD-260924-K7P2QM",
      "reservationId": "e2b9f0a4-3c5d-4e6f-8a7b-9c0d1e2f3a4b",
      "occurredAt": "2026-09-24T05:12:41+00:00",
      "correlationId": "4f1c2b9e-7d3a-4e8f-b6c5-a1d2e3f4a5b6",
      "fulfilmentType": "ScheduledDelivery",
      "status": "Confirmed",
      "orderDate": "2026-09-24T05:03:10+00:00",
      "shop": { "shopId": "11111111-1111-1111-1111-111111111111", "name": "Lake View Mini Mart", "ownerName": "Alice", "ownerEmail": "alice@example.com" },
      "agency": { "agencyId": "22222222-2222-2222-2222-222222222222", "name": "Colombo Agency", "email": "agency@example.com" },
      "territoryId": "33333333-3333-3333-3333-333333333333",
      "provinceId": "44444444-4444-4444-4444-444444444444",
      "salesRep": { "salesRepId": "55555555-5555-5555-5555-555555555555", "name": "Ruwan" },
      "lines": [{ "productId": "66666666-6666-6666-6666-666666666666", "productName": "Tea", "quantity": 2, "unitPrice": 1250.00, "lineTotal": 2500.00 }],
      "subtotal": 2500.00,
      "total": 2500.00,
      "currency": "LKR",
      "checkoutLocation": null,
      "confirmedAt": "2026-09-24T05:12:41+00:00"
    }
    """;

    [Fact]
    public void OrderConfirmedV1_MapsNestedPublisherFields()
    {
        var evt = OrderConfirmedWireEvent.Parse(ConfirmedJson(Guid.NewGuid()));

        evt.CompanyId.Should().Be(CompanyId);
        evt.OrderId.Should().Be(OrderId);
        evt.ShopId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        evt.ShopName.Should().Be("Lake View Mini Mart");
        evt.AgencyName.Should().Be("Colombo Agency");
        evt.OrderSalesRepId.Should().Be(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        evt.Lines.Should().ContainSingle();
    }

    [Fact]
    public void MalformedV1Event_FailsInsteadOfCreatingIncompleteDelivery()
    {
        var json = ConfirmedJson(Guid.NewGuid()).Replace("\"shop\": {", "\"ignoredShop\": {");
        Action parse = () => OrderConfirmedWireEvent.Parse(json);
        parse.Should().Throw<JsonException>();
    }

    [Fact]
    public void ImmediateCashSale_UsesPublisherCheckoutTimestamp()
    {
        var json = ConfirmedJson(Guid.NewGuid())
            .Replace("ScheduledDelivery", "ImmediateCashSale")
            .Replace("\"checkoutLocation\": null", "\"checkoutLocation\": { \"latitude\": 6.9165, \"longitude\": 79.8487, \"distanceMeters\": 42.18, \"accuracyMeters\": 8, \"checkedInAt\": \"2026-09-24T05:11:58+00:00\" }");

        var evt = OrderConfirmedWireEvent.Parse(json);

        evt.CheckoutLocation.Should().NotBeNull();
        evt.CheckoutLocation!.CheckedInAt.Should().Be(DateTimeOffset.Parse("2026-09-24T05:11:58+00:00"));
    }
}
