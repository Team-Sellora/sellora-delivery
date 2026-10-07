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
        var scheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var occurredAt = DateTimeOffset.UtcNow;
        var shopOwnerName = "Owner";
        var shopOwnerEmail = "test@shop.com";
        var agencyEmail = "test@agency.com";
        var previousStatus = DeliveryStatus.Pending.ToString();
        var correlationId = "CORR-123";
        var reason = "Assigned";
        var territoryId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var actorUserId = "user-123";
        var actorRole = "System";

        // Act
        var evt = DeliveryStatusChangedEvent.Create(
            companyId, deliveryJobId, orderId, orderReference,
            previousStatus, status, occurredAt, reason, scheduledDate,
            shopId, shopName, shopOwnerName, shopOwnerEmail,
            agencyId, agencyName, agencyEmail,
            deliveryReference, territoryId, provinceId,
            actorUserId, actorRole, correlationId);

        // Assert
        evt.CompanyId.Should().Be(companyId);
        evt.DeliveryId.Should().Be(deliveryJobId);
        evt.DeliveryReference.Should().Be(deliveryReference);
        evt.OrderId.Should().Be(orderId);
        evt.OrderReference.Should().Be(orderReference);
        evt.Agency.AgencyId.Should().Be(agencyId);
        evt.Agency.Name.Should().Be(agencyName);
        evt.Shop.ShopId.Should().Be(shopId);
        evt.Shop.Name.Should().Be(shopName);
        evt.Status.Should().Be(status);
        evt.PreviousStatus.Should().Be(previousStatus);
        evt.Reason.Should().Be(reason);
        evt.ScheduledFor.Should().Be(scheduledDate);
        evt.CorrelationId.Should().Be(correlationId);
        evt.TerritoryId.Should().Be(territoryId);
        evt.ProvinceId.Should().Be(provinceId);
        evt.Actor.UserId.Should().Be(actorUserId);
        evt.Actor.Role.Should().Be(actorRole);
        evt.OccurredAt.Should().Be(occurredAt);
    }
}
