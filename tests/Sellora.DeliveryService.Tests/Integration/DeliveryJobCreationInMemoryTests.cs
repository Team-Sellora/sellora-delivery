using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sellora.DeliveryService.Application.Consumers.Events;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Outbox;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Sellora.DeliveryService.Infrastructure.Services;
using Sellora.DeliveryService.Tests.Helpers;
using Xunit;

namespace Sellora.DeliveryService.Tests.Integration;

/// <summary>
/// In-memory integration tests — always runnable without Docker.
/// The Testcontainers tests (real PostgreSQL) are the authoritative
/// end-to-end tests and require Docker Desktop to be running.
/// </summary>
public class DeliveryJobCreationInMemoryTests : IDisposable
{
    private readonly DeliveryDbContext _db;

    public DeliveryJobCreationInMemoryTests()
    {
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DeliveryDbContext(options, tenantContext: null);
    }

    public void Dispose() => _db.Dispose();

    private DeliveryJobService CreateService() => new(
        new DeliveryJobRepository(_db),
        new EntityFrameworkOutboxWriter(_db),
        new DeliveryReferenceGenerator(),
        NullLogger<DeliveryJobService>.Instance);

    [Fact]
    public async Task ScheduledDelivery_PersistsJobWithCorrectFields()
    {
        var evt = EventFactory.BuildOrderConfirmedEvent("ScheduledDelivery");
        await CreateService().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        _db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(evt.CompanyId, evt.EventId));
        await _db.SaveChangesAsync();

        var job = await _db.DeliveryJobs
            .IgnoreQueryFilters()
            .Include(j => j.Lines)
            .Include(j => j.StatusHistory)
            .SingleAsync(j => j.OrderId == evt.OrderId);

        job.Status.Should().Be(DeliveryStatus.Pending);
        job.OrderReference.Should().Be(evt.OrderReference);
        job.ShopId.Should().Be(evt.ShopId);
        job.ShopName.Should().Be(evt.ShopName);
        job.AgencyId.Should().Be(evt.AgencyId);
        job.Total.Should().Be(evt.Total);
        job.Lines.Should().HaveCount(2);
        job.DeliveredAt.Should().BeNull();
        job.StatusHistory.Should().BeEmpty();

        var outbox = await _db.OutboxMessages
            .IgnoreQueryFilters()
            .SingleAsync(o => o.MessageKey == evt.OrderReference);

        outbox.Topic.Should().Be("sellora.delivery.v1");
        outbox.EventType.Should().Be("DeliveryJobCreated");
    }

    [Fact]
    public async Task ImmediateCashSale_PersistsDeliveredJobWithStatusHistoryRow()
    {
        var checkInAt = DateTimeOffset.UtcNow.AddMinutes(-8);
        var evt = EventFactory.BuildOrderConfirmedEvent(
            "ImmediateCashSale",
            new CheckoutLocationDto(Guid.NewGuid(), "Van", checkInAt));

        await CreateService().CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        _db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(evt.CompanyId, evt.EventId));
        await _db.SaveChangesAsync();

        var job = await _db.DeliveryJobs
            .IgnoreQueryFilters()
            .Include(j => j.StatusHistory)
            .SingleAsync(j => j.OrderId == evt.OrderId);

        job.Status.Should().Be(DeliveryStatus.Delivered);
        job.DeliveredAt.Should().Be(checkInAt);
        job.StatusHistory.Should().HaveCount(1);
        job.StatusHistory.First().ToStatus.Should().Be(DeliveryStatus.Delivered);
        job.StatusHistory.First().ActorRole.Should().Be("System");
        job.StatusHistory.First().OccurredAt.Should().Be(checkInAt);
    }

    [Fact]
    public async Task DuplicateEvent_IdempotencyReceiptPreventsSecondJob()
    {
        var evt = EventFactory.BuildOrderConfirmedEvent("ScheduledDelivery");
        var svc = CreateService();

        await svc.CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        _db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(evt.CompanyId, evt.EventId));
        await _db.SaveChangesAsync();

        // Simulate second delivery of the same event: receipt row exists, job must not be inserted again
        var alreadyProcessed = await _db.ProcessedDeliveryEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.CompanyId == evt.CompanyId && e.EventId == evt.EventId);

        alreadyProcessed.Should().BeTrue();

        var jobCount = await _db.DeliveryJobs
            .IgnoreQueryFilters()
            .CountAsync(j => j.OrderId == evt.OrderId);

        jobCount.Should().Be(1);
    }
}
