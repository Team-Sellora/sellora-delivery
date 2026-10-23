using System.Text.Json;
using FluentAssertions;
using Sellora.DeliveryService.Application.Events;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

/// <summary>
/// Contract test verifying that ReturnAcceptedEvent produced by DeliveryService
/// deserializes cleanly into a copy of InventoryService's real ReturnAcceptedEvent shape,
/// tolerating extra audit properties and accepting null BatchId.
/// </summary>
public class ReturnAcceptedEventContractTests
{
    // Exact replica of Sellora.InventoryService.Application.Events.ReturnAcceptedEvent
    private sealed record InventoryReturnAcceptedEvent(
        Guid EventId,
        string EventType,
        string SchemaVersion,
        Guid CompanyId,
        Guid EntityId,
        Guid InventoryOwnerId,
        string ReturnReference,
        IReadOnlyCollection<InventoryReturnAcceptedLine> Lines,
        DateTimeOffset AcceptedAt,
        string CorrelationId);

    // Exact replica of Sellora.InventoryService.Application.Events.ReturnAcceptedLine
    private sealed record InventoryReturnAcceptedLine(Guid ProductId, Guid? BatchId, int Quantity);

    private static readonly JsonSerializerOptions InventoryConsumerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void DeliveryReturnAcceptedEvent_DeserializesIntoInventoryReturnAcceptedEvent()
    {
        var companyId = Guid.NewGuid();
        var returnRequestId = Guid.NewGuid();
        var inventoryOwnerId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var acceptedAt = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid().ToString();

        var lines = new List<ReturnAcceptedLine>
        {
            new(productId1, null, 5, "Product 1"),
            new(productId2, null, 10, "Product 2")
        };

        var deliveryEvent = ReturnAcceptedEvent.Create(
            companyId: companyId,
            returnRequestId: returnRequestId,
            inventoryOwnerId: inventoryOwnerId,
            returnReference: "RT-261011-A1B2C3",
            acceptedAt: acceptedAt,
            correlationId: correlationId,
            deliveryId: deliveryId,
            orderReference: "ORD-998877",
            shopId: shopId,
            shopName: "City Shop",
            shopOwnerName: "Alice",
            shopOwnerEmail: "alice@shop.com",
            agencyId: agencyId,
            agencyName: "Metro Agency",
            agencyEmail: "agency@metro.com",
            lines: lines);

        var json = JsonSerializer.Serialize(deliveryEvent);

        var inventoryEvent = JsonSerializer.Deserialize<InventoryReturnAcceptedEvent>(json, InventoryConsumerOptions);

        inventoryEvent.Should().NotBeNull();
        inventoryEvent!.EventId.Should().Be(deliveryEvent.EventId);
        inventoryEvent.EventType.Should().Be("ReturnAccepted");
        inventoryEvent.SchemaVersion.Should().Be("1.0");
        inventoryEvent.CompanyId.Should().Be(companyId);
        inventoryEvent.EntityId.Should().Be(returnRequestId);
        inventoryEvent.InventoryOwnerId.Should().Be(inventoryOwnerId);
        inventoryEvent.ReturnReference.Should().Be("RT-261011-A1B2C3");
        inventoryEvent.AcceptedAt.Should().Be(acceptedAt);
        inventoryEvent.CorrelationId.Should().Be(correlationId);

        inventoryEvent.Lines.Should().HaveCount(2);
        var line1 = inventoryEvent.Lines.First(l => l.ProductId == productId1);
        line1.BatchId.Should().BeNull();
        line1.Quantity.Should().Be(5);

        var line2 = inventoryEvent.Lines.First(l => l.ProductId == productId2);
        line2.BatchId.Should().BeNull();
        line2.Quantity.Should().Be(10);
    }
}
