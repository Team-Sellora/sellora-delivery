using FluentAssertions;
using Sellora.DeliveryService.Application.Events;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryStatusChangedEventTests
{
    [Fact]
    public void Create_WhenCalled_SetsAllPropertiesCorrectly()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var deliveryJobId = Guid.NewGuid();
        var deliveryReference = "REF-123";
        var orderId = Guid.NewGuid();
        var orderReference = "ORD-456";
        var fulfilmentType = "Delivery";
        var agencyId = Guid.NewGuid();
        var agencyName = "Test Agency";
        var shopId = Guid.NewGuid();
        var shopName = "Test Shop";
        var status = DeliveryStatus.Assigned.ToString();
        var assignedRepId = Guid.NewGuid();
        var assignedRepName = "John Doe";
        var scheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var deliveredAt = DateTimeOffset.UtcNow;
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        var evt = DeliveryStatusChangedEvent.Create(
            companyId, deliveryJobId, deliveryReference, orderId, orderReference,
            fulfilmentType, agencyId, agencyName, shopId, shopName, status,
            assignedRepId, assignedRepName, scheduledDate, deliveredAt, occurredAt);

        // Assert
        evt.CompanyId.Should().Be(companyId);
        evt.DeliveryJobId.Should().Be(deliveryJobId);
        evt.DeliveryReference.Should().Be(deliveryReference);
        evt.OrderId.Should().Be(orderId);
        evt.OrderReference.Should().Be(orderReference);
        evt.FulfilmentType.Should().Be(fulfilmentType);
        evt.AgencyId.Should().Be(agencyId);
        evt.AgencyName.Should().Be(agencyName);
        evt.ShopId.Should().Be(shopId);
        evt.ShopName.Should().Be(shopName);
        evt.Status.Should().Be(status);
        evt.AssignedRepId.Should().Be(assignedRepId);
        evt.AssignedRepName.Should().Be(assignedRepName);
        evt.ScheduledDate.Should().Be(scheduledDate);
        evt.DeliveredAt.Should().Be(deliveredAt);
        evt.OccurredAt.Should().Be(occurredAt);
    }
}
