using System.Text.RegularExpressions;
using FluentAssertions;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Services;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class ReturnRequestTests
{
    [Fact]
    public void Create_InitializesRequestedReturn_WithLines()
    {
        var companyId = Guid.NewGuid();
        var deliveryJobId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var territoryId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var line = new ReturnRequestLine(Guid.Empty, companyId, Guid.NewGuid(), "Item A", 5, "Damaged in transit");
        var returnRequest = ReturnRequest.Create(
            companyId,
            "RT-261011-A1B2C3",
            deliveryJobId,
            orderId,
            "ORD-12345",
            shopId,
            agencyId,
            territoryId,
            provinceId,
            "user-1",
            "ShopOwner",
            now,
            new[] { line });

        returnRequest.ReturnRequestId.Should().NotBeEmpty();
        returnRequest.Status.Should().Be(ReturnStatus.Requested);
        returnRequest.ReturnReference.Should().Be("RT-261011-A1B2C3");
        returnRequest.Lines.Should().HaveCount(1);
        returnRequest.Lines.First().Quantity.Should().Be(5);
        returnRequest.InventoryOwnerId.Should().BeNull();
        returnRequest.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void Approve_WhenRequested_UpdatesStatusAndInventoryOwner()
    {
        var request = CreateSampleReturn();
        var inventoryOwnerId = Guid.NewGuid();
        var decidedAt = DateTimeOffset.UtcNow;

        request.Approve(inventoryOwnerId, "op-1", "AgencyOperator", decidedAt);

        request.Status.Should().Be(ReturnStatus.Approved);
        request.InventoryOwnerId.Should().Be(inventoryOwnerId);
        request.DecidedByUserId.Should().Be("op-1");
        request.DecidedByRole.Should().Be("AgencyOperator");
        request.DecidedAt.Should().Be(decidedAt);
    }

    [Fact]
    public void Approve_WhenAlreadyDecided_ThrowsInvalidOperationException()
    {
        var request = CreateSampleReturn();
        request.Approve(Guid.NewGuid(), "op-1", "AgencyOperator", DateTimeOffset.UtcNow);

        var act = () => request.Approve(Guid.NewGuid(), "op-2", "AgencyOperator", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot approve return request in status 'Approved'*");
    }

    [Fact]
    public void Reject_WhenRequested_UpdatesStatusAndReason()
    {
        var request = CreateSampleReturn();
        var decidedAt = DateTimeOffset.UtcNow;

        request.Reject("Goods opened and used", "op-1", "AgencyOperator", decidedAt);

        request.Status.Should().Be(ReturnStatus.Rejected);
        request.DecisionReason.Should().Be("Goods opened and used");
        request.DecidedByUserId.Should().Be("op-1");
        request.DecidedByRole.Should().Be("AgencyOperator");
        request.DecidedAt.Should().Be(decidedAt);
        request.InventoryOwnerId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Reject_WithoutReason_ThrowsArgumentException(string? reason)
    {
        var request = CreateSampleReturn();

        var act = () => request.Reject(reason!, "op-1", "AgencyOperator", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reject_WhenAlreadyDecided_ThrowsInvalidOperationException()
    {
        var request = CreateSampleReturn();
        request.Reject("Reason 1", "op-1", "AgencyOperator", DateTimeOffset.UtcNow);

        var act = () => request.Reject("Reason 2", "op-2", "AgencyOperator", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot reject return request in status 'Rejected'*");
    }

    [Fact]
    public void ReturnReferenceGenerator_GeneratesCorrectFormat()
    {
        var generator = new ReturnReferenceGenerator();
        var at = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

        var reference = generator.Generate(at);

        reference.Should().StartWith("RT-261011-");
        Regex.IsMatch(reference, @"^RT-\d{6}-[0-9A-F]{6}$").Should().BeTrue();
    }

    private static ReturnRequest CreateSampleReturn()
    {
        return ReturnRequest.Create(
            Guid.NewGuid(),
            "RT-261011-123456",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user-1",
            "ShopOwner",
            DateTimeOffset.UtcNow,
            []);
    }
}
