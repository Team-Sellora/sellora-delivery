using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sellora.DeliveryService.Application.Consumers.Events;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Sellora.DeliveryService.Infrastructure.Services;
using Sellora.DeliveryService.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sellora.DeliveryService.Tests.Integration;

[Collection("PostgreSql")]
public class DeliveryJobCreationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("delivery_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private DeliveryDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new DeliveryDbContext(options, tenantContext: null);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private DeliveryJobService CreateService(DeliveryDbContext db)
    {
        var repo = new DeliveryJobRepository(db);
        var outboxWriter = new Infrastructure.Outbox.EntityFrameworkOutboxWriter(db);
        var refGen = new DeliveryReferenceGenerator();
        var logger = NullLogger<DeliveryJobService>.Instance;
        return new DeliveryJobService(repo, outboxWriter, refGen, logger);
    }

    [Fact]
    public async Task ScheduledDelivery_PersistsJobWithCorrectDataAndOutboxMessage()
    {
        var evt = EventFactory.BuildOrderConfirmedEvent("ScheduledDelivery");
        var sut = CreateService(_db);

        await sut.CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        _db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(evt.CompanyId, evt.EventId));
        await _db.SaveChangesAsync();

        var job = await _db.DeliveryJobs
            .IgnoreQueryFilters()
            .Include(j => j.Lines)
            .Include(j => j.StatusHistory)
            .SingleOrDefaultAsync(j => j.OrderId == evt.OrderId);

        job.Should().NotBeNull();
        job!.Status.Should().Be(DeliveryStatus.Pending);
        job.OrderReference.Should().Be(evt.OrderReference);
        job.ShopName.Should().Be(evt.ShopName);
        job.AgencyId.Should().Be(evt.AgencyId);
        job.Total.Should().Be(evt.Total);
        job.Lines.Should().HaveCount(2);
        job.DeliveredAt.Should().BeNull();
        job.StatusHistory.Should().BeEmpty();

        var outbox = await _db.OutboxMessages
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(o => o.MessageKey == evt.OrderReference);

        outbox.Should().NotBeNull();
        outbox!.Topic.Should().Be("sellora.delivery.v1");
        outbox.EventType.Should().Be("DeliveryJobCreated");
    }

    [Fact]
    public async Task ImmediateCashSale_PersistsDeliveredJobWithCheckoutTimestamp()
    {
        var checkInAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var evt = EventFactory.BuildOrderConfirmedEvent(
            "ImmediateCashSale",
            new CheckoutLocationDto(Guid.NewGuid(), "Van Stop 3", checkInAt));

        var sut = CreateService(_db);
        await sut.CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        _db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(evt.CompanyId, evt.EventId));
        await _db.SaveChangesAsync();

        var job = await _db.DeliveryJobs
            .IgnoreQueryFilters()
            .Include(j => j.StatusHistory)
            .SingleAsync(j => j.OrderId == evt.OrderId);

        job.Status.Should().Be(DeliveryStatus.Delivered);
        job.DeliveredAt.Should().BeCloseTo(checkInAt, TimeSpan.FromSeconds(1));
        job.StatusHistory.Should().HaveCount(1);
        job.StatusHistory.First().ToStatus.Should().Be(DeliveryStatus.Delivered);
    }

    [Fact]
    public async Task DuplicateEvent_ProcessedReceiptPreventsSecondJobInsert()
    {
        var evt = EventFactory.BuildOrderConfirmedEvent("ScheduledDelivery");
        var sut = CreateService(_db);

        // First processing
        await sut.CreateFromOrderConfirmedAsync(evt, CancellationToken.None);
        _db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(evt.CompanyId, evt.EventId));
        await _db.SaveChangesAsync();

        // Second processing — the idempotency receipt is already there; the consumer skips AddAsync
        var alreadyProcessed = await _db.ProcessedDeliveryEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.CompanyId == evt.CompanyId && e.EventId == evt.EventId);

        alreadyProcessed.Should().BeTrue("receipt row must exist to gate the second attempt");

        var jobCount = await _db.DeliveryJobs
            .IgnoreQueryFilters()
            .CountAsync(j => j.OrderId == evt.OrderId);

        jobCount.Should().Be(1);
    }
}
