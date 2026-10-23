using FluentAssertions;
using Sellora.DeliveryService.Application.Events;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryConfirmationEntityAndEventTests
{
    [Fact]
    public void DeliveryConfirmation_Create_SetsPropertiesCorrectly()
    {
        var jobId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var type = ConfirmationType.Confirmed;
        var notes = "Items verified in good condition";
        var signatureUrl = "https://signatures.sellora.com/sig.png";

        var confirmation = DeliveryConfirmation.Create(
            jobId,
            companyId,
            type,
            notes,
            signatureUrl
        );

        confirmation.DeliveryJobId.Should().Be(jobId);
        confirmation.CompanyId.Should().Be(companyId);
        confirmation.Type.Should().Be(type);
        confirmation.Notes.Should().Be(notes);
        confirmation.SignatureUrl.Should().Be(signatureUrl);
        confirmation.ConfirmedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DeliveryConfirmedEvent_Create_SetsPropertiesCorrectly()
    {
        var companyId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderRef = "ORD-001";
        var confirmedAt = DateTimeOffset.UtcNow;
        var note = "All good";
        var signatureUrl = "https://example.com/sig.png";
        var shopId = Guid.NewGuid();
        var shopName = "Best Shop";
        var shopOwnerName = "John Doe";
        var shopOwnerEmail = "john@example.com";
        var agencyId = Guid.NewGuid();
        var agencyName = "City Agency";
        var agencyEmail = "agency@example.com";
        var deliveryRef = "DEL-001";
        var territoryId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var actorUserId = "user-1";
        var actorRole = "ShopOwner";
        var correlationId = "corr-1";

        var evt = DeliveryConfirmedEvent.Create(
            companyId,
            deliveryId,
            orderId,
            orderRef,
            confirmedAt,
            note,
            signatureUrl,
            shopId,
            shopName,
            shopOwnerName,
            shopOwnerEmail,
            agencyId,
            agencyName,
            agencyEmail,
            deliveryRef,
            territoryId,
            provinceId,
            actorUserId,
            actorRole,
            correlationId
        );

        evt.CompanyId.Should().Be(companyId);
        evt.DeliveryId.Should().Be(deliveryId);
        evt.OrderId.Should().Be(orderId);
        evt.OrderReference.Should().Be(orderRef);
        evt.ConfirmedAt.Should().Be(confirmedAt);
        evt.Note.Should().Be(note);
        evt.SignatureUrl.Should().Be(signatureUrl);
        evt.Shop.ShopId.Should().Be(shopId);
        evt.Shop.Name.Should().Be(shopName);
        evt.Shop.OwnerName.Should().Be(shopOwnerName);
        evt.Shop.OwnerEmail.Should().Be(shopOwnerEmail);
        evt.Agency.AgencyId.Should().Be(agencyId);
        evt.Agency.Name.Should().Be(agencyName);
        evt.Agency.Email.Should().Be(agencyEmail);
        evt.DeliveryReference.Should().Be(deliveryRef);
        evt.TerritoryId.Should().Be(territoryId);
        evt.ProvinceId.Should().Be(provinceId);
        evt.Actor.UserId.Should().Be(actorUserId);
        evt.Actor.Role.Should().Be(actorRole);
        evt.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public void DeliveryDisputedEvent_Create_SetsPropertiesCorrectly()
    {
        var companyId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderRef = "ORD-002";
        var disputedAt = DateTimeOffset.UtcNow;
        var note = "Box damaged upon arrival";
        var signatureUrl = "https://example.com/sig2.png";
        var shopId = Guid.NewGuid();
        var shopName = "Best Shop 2";
        var shopOwnerName = "Jane Doe";
        var shopOwnerEmail = "jane@example.com";
        var agencyId = Guid.NewGuid();
        var agencyName = "City Agency 2";
        var agencyEmail = "agency2@example.com";
        var deliveryRef = "DEL-002";
        var territoryId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var actorUserId = "user-2";
        var actorRole = "ShopOwner";
        var correlationId = "corr-2";

        var evt = DeliveryDisputedEvent.Create(
            companyId,
            deliveryId,
            orderId,
            orderRef,
            disputedAt,
            note,
            signatureUrl,
            shopId,
            shopName,
            shopOwnerName,
            shopOwnerEmail,
            agencyId,
            agencyName,
            agencyEmail,
            deliveryRef,
            territoryId,
            provinceId,
            actorUserId,
            actorRole,
            correlationId
        );

        evt.CompanyId.Should().Be(companyId);
        evt.DeliveryId.Should().Be(deliveryId);
        evt.OrderId.Should().Be(orderId);
        evt.OrderReference.Should().Be(orderRef);
        evt.DisputedAt.Should().Be(disputedAt);
        evt.Note.Should().Be(note);
        evt.SignatureUrl.Should().Be(signatureUrl);
        evt.Shop.ShopId.Should().Be(shopId);
        evt.Shop.Name.Should().Be(shopName);
        evt.Shop.OwnerName.Should().Be(shopOwnerName);
        evt.Shop.OwnerEmail.Should().Be(shopOwnerEmail);
        evt.Agency.AgencyId.Should().Be(agencyId);
        evt.Agency.Name.Should().Be(agencyName);
        evt.Agency.Email.Should().Be(agencyEmail);
        evt.DeliveryReference.Should().Be(deliveryRef);
        evt.TerritoryId.Should().Be(territoryId);
        evt.ProvinceId.Should().Be(provinceId);
        evt.Actor.UserId.Should().Be(actorUserId);
        evt.Actor.Role.Should().Be(actorRole);
        evt.CorrelationId.Should().Be(correlationId);
    }
}
