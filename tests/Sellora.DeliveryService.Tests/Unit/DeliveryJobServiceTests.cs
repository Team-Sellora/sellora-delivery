using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Consumers.Events;
using Sellora.DeliveryService.Application.Events;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using System.Text.Json;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryJobServiceTests
{
    private readonly Mock<IDeliveryJobRepository> _repositoryMock = new();
    private readonly Mock<IOutboxWriter> _outboxWriterMock = new();
    private readonly Mock<IDeliveryReferenceGenerator> _refGenMock = new();
    private readonly Mock<ILogger<DeliveryJobService>> _loggerMock = new();

    private DeliveryJobService CreateSut() => new(
        _repositoryMock.Object,
        _outboxWriterMock.Object,
        _refGenMock.Object,
        _loggerMock.Object);

    private static OrderConfirmedEvent BuildEvent(
        string fulfilmentType = "ScheduledDelivery",
        CheckoutLocationDto? checkoutLocation = null)
    {
        var orderId = Guid.NewGuid();
        return new OrderConfirmedEvent(
            EventId: Guid.NewGuid(),
            EventType: "OrderConfirmed",
            CompanyId: Guid.NewGuid(),
            OrderId: orderId,
            OrderReference: "ORD-241010-AABBCC",
            OccurredAt: DateTimeOffset.UtcNow,
            FulfilmentType: fulfilmentType,
            ShopId: Guid.NewGuid(),
            ShopName: "Test Shop",
            ShopOwnerName: "John Doe",
            ShopOwnerEmail: "john@example.com",
            AgencyId: Guid.NewGuid(),
            AgencyName: "Test Agency",
            AgencyEmail: "agency@example.com",
            TerritoryId: Guid.NewGuid(),
            ProvinceId: Guid.NewGuid(),
            OrderSalesRepId: Guid.NewGuid(),
            Total: 1500.00m,
            Currency: "MWK",
            Lines: new List<OrderLineDto>
            {
                new(Guid.NewGuid(), "Product A", 10, 100m, 1000m),
                new(Guid.NewGuid(), "Product B", 5, 100m, 500m)
            },
            CheckoutLocation: checkoutLocation,
            ConfirmedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task ScheduledDelivery_CreatesJobWithStatusPending()
    {
        _refGenMock.Setup(g => g.Generate(It.IsAny<DateTimeOffset>())).Returns("DL-241010-AABBCC");

        DeliveryJob? capturedJob = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DeliveryJob>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryJob, CancellationToken>((j, _) => capturedJob = j)
            .Returns(Task.CompletedTask);

        var evt = BuildEvent("ScheduledDelivery");
        await CreateSut().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);

        capturedJob.Should().NotBeNull();
        capturedJob!.Status.Should().Be(DeliveryStatus.Pending);
        capturedJob.OrderId.Should().Be(evt.OrderId);
        capturedJob.OrderReference.Should().Be(evt.OrderReference);
        capturedJob.ShopName.Should().Be(evt.ShopName);
        capturedJob.AgencyId.Should().Be(evt.AgencyId);
        capturedJob.Total.Should().Be(evt.Total);
        capturedJob.Lines.Should().HaveCount(2);
        capturedJob.DeliveredAt.Should().BeNull();
        capturedJob.StatusHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task ImmediateCashSale_CreatesJobWithStatusDelivered()
    {
        _refGenMock.Setup(g => g.Generate(It.IsAny<DateTimeOffset>())).Returns("DL-241010-CCDDEE");

        DeliveryJob? capturedJob = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DeliveryJob>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryJob, CancellationToken>((j, _) => capturedJob = j)
            .Returns(Task.CompletedTask);

        var checkinAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var evt = BuildEvent("ImmediateCashSale", new CheckoutLocationDto(Guid.NewGuid(), "Van", checkinAt));

        await CreateSut().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);

        capturedJob!.Status.Should().Be(DeliveryStatus.Delivered);
        capturedJob.DeliveredAt.Should().Be(checkinAt);
        capturedJob.StatusHistory.Should().HaveCount(1);
        capturedJob.StatusHistory.First().ToStatus.Should().Be(DeliveryStatus.Delivered);
        capturedJob.StatusHistory.First().ActorRole.Should().Be("System");
    }

    [Fact]
    public async Task ImmediateCashSale_WhenCheckInAtNull_UsesConfirmedAt()
    {
        _refGenMock.Setup(g => g.Generate(It.IsAny<DateTimeOffset>())).Returns("DL-241010-XXYYZZ");

        DeliveryJob? capturedJob = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DeliveryJob>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryJob, CancellationToken>((j, _) => capturedJob = j)
            .Returns(Task.CompletedTask);

        var evt = BuildEvent("ImmediateCashSale", checkoutLocation: null);

        await CreateSut().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);

        capturedJob!.Status.Should().Be(DeliveryStatus.Delivered);
        // Must fall back to ConfirmedAt when CheckedInAt is absent
        capturedJob.DeliveredAt.Should().Be(evt.ConfirmedAt);
    }

    [Fact]
    public async Task ScheduledDelivery_WritesOutboxMessageWithCorrectEventType()
    {
        _refGenMock.Setup(g => g.Generate(It.IsAny<DateTimeOffset>())).Returns("DL-241010-112233");
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DeliveryJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        OutboxMessage? capturedOutbox = null;
        _outboxWriterMock.Setup(w => w.Write(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(m => capturedOutbox = m);

        var evt = BuildEvent("ScheduledDelivery");
        await CreateSut().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);

        capturedOutbox.Should().NotBeNull();
        capturedOutbox!.Topic.Should().Be("sellora.delivery.v1");
        capturedOutbox.EventType.Should().Be("DeliveryJobCreated");
        capturedOutbox.MessageKey.Should().Be(evt.OrderReference);

        var outboundEvt = JsonSerializer.Deserialize<DeliveryJobCreatedEvent>(capturedOutbox.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        outboundEvt!.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task ImmediateCashSale_PublishesDeliveryJobCreatedNotDeliveryStatusChanged()
    {
        // Publishing DeliveryStatusChanged here would cause Notification to send a duplicate
        // "delivered" email — only DeliveryJobCreated is published for the initial event.
        _refGenMock.Setup(g => g.Generate(It.IsAny<DateTimeOffset>())).Returns("DL-241010-FFEEDD");
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DeliveryJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        OutboxMessage? capturedOutbox = null;
        _outboxWriterMock.Setup(w => w.Write(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(m => capturedOutbox = m);

        var evt = BuildEvent("ImmediateCashSale", new CheckoutLocationDto(Guid.NewGuid(), "Van", DateTimeOffset.UtcNow));
        await CreateSut().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);

        capturedOutbox!.EventType.Should().Be("DeliveryJobCreated");
    }
}
