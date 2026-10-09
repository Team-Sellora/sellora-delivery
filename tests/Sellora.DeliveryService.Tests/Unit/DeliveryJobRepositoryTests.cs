using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryJobRepositoryTests : IDisposable
{
    private readonly DeliveryDbContext _db;
    private readonly DeliveryJobRepository _repository;
    private readonly Guid _companyId = Guid.NewGuid();

    private class FakeTenantContext(Guid companyId) : Sellora.DeliveryService.Application.Common.Interfaces.ITenantContext
    {
        public Guid CompanyId { get; } = companyId;
    }

    public DeliveryJobRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DeliveryDbContext(options, new FakeTenantContext(_companyId));
        _repository = new DeliveryJobRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private DeliveryJob CreateJob(
        Guid agencyId,
        Guid shopId,
        Guid provinceId,
        Guid? repId = null,
        DateOnly? scheduledDate = null,
        string fulfilmentType = "ScheduledDelivery",
        Guid? orderId = null)
    {
        var line = new DeliveryJobLine(
            Guid.NewGuid(), _companyId, Guid.NewGuid(), "Sample Product", 2, 50m, 100m);

        var job = DeliveryJob.CreatePending(
            _companyId,
            $"DEL-{Guid.NewGuid():N}"[..12],
            orderId ?? Guid.NewGuid(),
            $"ORD-{Guid.NewGuid():N}"[..12],
            fulfilmentType,
            shopId,
            "Shop A",
            "Owner A",
            "owner@test.com",
            agencyId,
            "Agency A",
            "agency@test.com",
            Guid.NewGuid(),
            provinceId,
            Guid.NewGuid(),
            100m,
            "LKR",
            [line]);

        if (repId.HasValue && scheduledDate.HasValue)
        {
            job.Assign(repId.Value, "Rep A", scheduledDate.Value, "actor-1", "AreaManager");
        }

        return job;
    }

    private DeliveryJob CreateCashSaleJob(Guid agencyId, Guid shopId, Guid provinceId)
    {
        var line = new DeliveryJobLine(
            Guid.NewGuid(), _companyId, Guid.NewGuid(), "Instant Product", 1, 200m, 200m);

        return DeliveryJob.CreateDelivered(
            _companyId,
            $"DEL-{Guid.NewGuid():N}"[..12],
            Guid.NewGuid(),
            $"ORD-{Guid.NewGuid():N}"[..12],
            "ImmediateCashSale",
            shopId,
            "Shop Direct",
            "Owner Direct",
            "direct@test.com",
            agencyId,
            "Agency Direct",
            "agency@test.com",
            Guid.NewGuid(),
            provinceId,
            Guid.NewGuid(),
            200m,
            "LKR",
            DateTimeOffset.UtcNow,
            [line],
            "system-actor");
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ReturnsJobWithLinesAndStatusHistory_WhenExists()
    {
        var repId = Guid.NewGuid();
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), repId, new DateOnly(2026, 10, 15));
        _db.DeliveryJobs.Add(job);
        await _db.SaveChangesAsync();

        var result = await _repository.GetByIdWithDetailsAsync(job.DeliveryJobId);

        result.Should().NotBeNull();
        result!.DeliveryJobId.Should().Be(job.DeliveryJobId);
        result.Lines.Should().HaveCount(1);
        result.StatusHistory.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repository.GetByIdWithDetailsAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WhenCompanyAdmin_ReturnsAllWithoutScopeRestrictions()
    {
        var job1 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var job2 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery { IsCompanyAdmin = true, Page = 1, PageSize = 10, IncludeHandovers = true };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_ScopeShopId_FiltersByShop()
    {
        var targetShop = Guid.NewGuid();
        var job1 = CreateJob(Guid.NewGuid(), targetShop, Guid.NewGuid());
        var job2 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery { ScopeShopId = targetShop, Page = 1, PageSize = 10, IncludeHandovers = true };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(job1.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_ScopeSalesRepId_FiltersByAssignedRep()
    {
        var repId = Guid.NewGuid();
        var job1 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), repId, new DateOnly(2026, 10, 15));
        var job2 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery { ScopeSalesRepId = repId, Page = 1, PageSize = 10, IncludeHandovers = true };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(job1.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_ScopeAgencyId_FiltersByAgency()
    {
        var targetAgency = Guid.NewGuid();
        var job1 = CreateJob(targetAgency, Guid.NewGuid(), Guid.NewGuid());
        var job2 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery { ScopeAgencyId = targetAgency, Page = 1, PageSize = 10, IncludeHandovers = true };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(job1.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_ScopeProvinceIds_FiltersByProvince()
    {
        var targetProvince = Guid.NewGuid();
        var job1 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), targetProvince);
        var job2 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery { ScopeProvinceIds = [targetProvince], Page = 1, PageSize = 10, IncludeHandovers = true };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(job1.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_Fallback_WhenNotAdminAndNoScope_ReturnsEmpty()
    {
        var job1 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.Add(job1);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery { IsCompanyAdmin = false, Page = 1, PageSize = 10, IncludeHandovers = true };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_FiltersByStatuses()
    {
        var repId = Guid.NewGuid();
        var pendingJob = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var assignedJob = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), repId, new DateOnly(2026, 10, 15));
        _db.DeliveryJobs.AddRange(pendingJob, assignedJob);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery
        {
            IsCompanyAdmin = true,
            Statuses = ["Assigned", "NonExistentStatus"],
            Page = 1,
            PageSize = 10,
            IncludeHandovers = true
        };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(assignedJob.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_FiltersByScheduledDateRange()
    {
        var repId = Guid.NewGuid();
        var job1 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), repId, new DateOnly(2026, 10, 10));
        var job2 = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), repId, new DateOnly(2026, 10, 20));
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery
        {
            IsCompanyAdmin = true,
            ScheduledDateFrom = new DateOnly(2026, 10, 15),
            ScheduledDateTo = new DateOnly(2026, 10, 25),
            Page = 1,
            PageSize = 10,
            IncludeHandovers = true
        };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(job2.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_FiltersBySalesRepId_ShopId_And_OrderId()
    {
        var repId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var matchingJob = CreateJob(Guid.NewGuid(), shopId, Guid.NewGuid(), repId, new DateOnly(2026, 10, 12), orderId: orderId);
        var otherJob = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _db.DeliveryJobs.AddRange(matchingJob, otherJob);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery
        {
            IsCompanyAdmin = true,
            SalesRepId = repId,
            ShopId = shopId,
            OrderId = orderId,
            Page = 1,
            PageSize = 10,
            IncludeHandovers = true
        };
        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items[0].DeliveryJobId.Should().Be(matchingJob.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_PagingAndHandoverFlags()
    {
        var agencyId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var job1 = CreateJob(agencyId, shopId, provinceId);
        var job2 = CreateJob(agencyId, shopId, provinceId);
        var cashJob = CreateCashSaleJob(agencyId, shopId, provinceId);
        _db.DeliveryJobs.AddRange(job1, job2, cashJob);
        await _db.SaveChangesAsync();

        // IncludeHandovers = false should exclude cashJob
        var queryWithoutHandovers = new DeliveryJobQuery
        {
            IsCompanyAdmin = true,
            IncludeHandovers = false,
            Page = 1,
            PageSize = 10
        };
        var (itemsWithoutHandovers, countWithout) = await _repository.ListAsync(queryWithoutHandovers);
        countWithout.Should().Be(2);
        itemsWithoutHandovers.Should().NotContain(j => j.DeliveryJobId == cashJob.DeliveryJobId);

        // Paging: page 2, pageSize 1
        var pagedQuery = new DeliveryJobQuery
        {
            IsCompanyAdmin = true,
            IncludeHandovers = true,
            Page = 2,
            PageSize = 1
        };
        var (pagedItems, totalCount) = await _repository.ListAsync(pagedQuery);
        totalCount.Should().Be(3);
        pagedItems.Should().HaveCount(1);
    }
}
