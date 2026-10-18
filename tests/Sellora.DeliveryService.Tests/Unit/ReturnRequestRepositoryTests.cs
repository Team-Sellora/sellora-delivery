using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class ReturnRequestRepositoryTests : IDisposable
{
    private readonly DeliveryDbContext _db;
    private readonly ReturnRequestRepository _repository;
    private readonly Guid _companyId = Guid.NewGuid();

    private class FakeTenantContext(Guid companyId) : ITenantContext
    {
        public Guid CompanyId { get; } = companyId;
    }

    public ReturnRequestRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DeliveryDbContext(options, new FakeTenantContext(_companyId));
        _repository = new ReturnRequestRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private DeliveryJob CreateJob(
        Guid agencyId,
        Guid shopId,
        Guid provinceId,
        Guid? repId = null,
        Guid? orderRepId = null)
    {
        var job = DeliveryJob.CreatePending(
            _companyId,
            $"DEL-{Guid.NewGuid():N}"[..12],
            Guid.NewGuid(),
            $"ORD-{Guid.NewGuid():N}"[..12],
            "ScheduledDelivery",
            shopId,
            "Sample Shop",
            "Shop Owner",
            "shop@example.com",
            agencyId,
            "Sample Agency",
            "agency@example.com",
            Guid.NewGuid(),
            provinceId,
            orderRepId ?? Guid.NewGuid(),
            200m,
            "LKR",
            [new DeliveryJobLine(Guid.NewGuid(), _companyId, Guid.NewGuid(), "Item", 5, 40m, 200m)]);

        if (repId.HasValue)
        {
            job.Assign(repId.Value, "Rep Name", DateOnly.FromDateTime(DateTime.UtcNow), "user1", "Dispatcher");
        }

        return job;
    }

    private ReturnRequest CreateReturn(
        DeliveryJob job,
        ReturnStatus status = ReturnStatus.Requested,
        DateTimeOffset? requestedAt = null,
        Guid? productId = null,
        int quantity = 2)
    {
        var pId = productId ?? Guid.NewGuid();
        var lines = new[] { new ReturnRequestLine(Guid.NewGuid(), _companyId, pId, "Product", quantity, "Damaged") };
        var returnReq = ReturnRequest.Create(
            _companyId,
            $"RT-{Guid.NewGuid():N}"[..12],
            job.DeliveryJobId,
            job.OrderId,
            job.OrderReference,
            job.ShopId,
            job.AgencyId,
            job.TerritoryId,
            job.ProvinceId,
            "user1",
            "ShopOwner",
            requestedAt ?? DateTimeOffset.UtcNow,
            lines);

        if (status == ReturnStatus.Approved)
        {
            returnReq.Approve(Guid.NewGuid(), "operator1", "AgencyOperator", DateTimeOffset.UtcNow);
        }
        else if (status == ReturnStatus.Rejected)
        {
            returnReq.Reject("Bad request", "operator1", "AgencyOperator", DateTimeOffset.UtcNow);
        }

        return returnReq;
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_ShouldPersistAndRetrieve()
    {
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await _db.DeliveryJobs.AddAsync(job);
        await _db.SaveChangesAsync();

        var ret = CreateReturn(job);
        await _repository.AddAsync(ret);
        await _repository.SaveChangesAsync();

        var retrieved = await _repository.GetByIdAsync(ret.ReturnRequestId);
        retrieved.Should().NotBeNull();
        retrieved!.ReturnReference.Should().Be(ret.ReturnReference);

        var retrievedWithLines = await _repository.GetByIdWithLinesAsync(ret.ReturnRequestId);
        retrievedWithLines.Should().NotBeNull();
        retrievedWithLines!.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetReturnedQuantitiesByJobIdAsync_ShouldSumApprovedAndRequested_AndRespectExclude()
    {
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await _db.DeliveryJobs.AddAsync(job);
        await _db.SaveChangesAsync();

        var productId = Guid.NewGuid();
        var ret1 = CreateReturn(job, ReturnStatus.Requested, null, productId, 3);
        var ret2 = CreateReturn(job, ReturnStatus.Approved, null, productId, 5);
        var ret3 = CreateReturn(job, ReturnStatus.Rejected, null, productId, 2); // Should be ignored

        await _repository.AddAsync(ret1);
        await _repository.AddAsync(ret2);
        await _repository.AddAsync(ret3);
        await _repository.SaveChangesAsync();

        var quantities = await _repository.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId);
        quantities.Should().ContainKey(productId);
        quantities[productId].Should().Be(8);

        var excluded = await _repository.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, ret1.ReturnRequestId);
        excluded[productId].Should().Be(5);
    }

    [Fact]
    public async Task ListAsync_CompanyAdmin_ShouldReturnAllMatchingFilters()
    {
        var agencyId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var job = CreateJob(agencyId, shopId, provinceId);
        await _db.DeliveryJobs.AddAsync(job);
        await _db.SaveChangesAsync();

        var ret1 = CreateReturn(job, ReturnStatus.Requested, DateTimeOffset.UtcNow.AddMinutes(-10));
        var ret2 = CreateReturn(job, ReturnStatus.Approved, DateTimeOffset.UtcNow);
        await _repository.AddAsync(ret1);
        await _repository.AddAsync(ret2);
        await _repository.SaveChangesAsync();

        var query = new ReturnRequestQuery
        {
            IsCompanyAdmin = true,
            Page = 1,
            PageSize = 10,
            Statuses = ["Requested", "Approved"]
        };

        var (items, totalCount) = await _repository.ListAsync(query);
        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].Status.Should().Be(ReturnStatus.Requested); // Requested ordered first
    }

    [Fact]
    public async Task ListAsync_RoleScopes_ShouldFilterCorrectly()
    {
        var agency1 = Guid.NewGuid();
        var agency2 = Guid.NewGuid();
        var shop1 = Guid.NewGuid();
        var shop2 = Guid.NewGuid();
        var prov1 = Guid.NewGuid();
        var rep1 = Guid.NewGuid();

        var job1 = CreateJob(agency1, shop1, prov1, repId: rep1);
        var job2 = CreateJob(agency2, shop2, prov1);
        await _db.DeliveryJobs.AddRangeAsync(job1, job2);
        await _db.SaveChangesAsync();

        var ret1 = CreateReturn(job1);
        var ret2 = CreateReturn(job2);
        await _repository.AddAsync(ret1);
        await _repository.AddAsync(ret2);
        await _repository.SaveChangesAsync();

        // Shop scope
        var shopQuery = new ReturnRequestQuery { ScopeShopId = shop1, Page = 1, PageSize = 10 };
        var (shopItems, _) = await _repository.ListAsync(shopQuery);
        shopItems.Should().ContainSingle(x => x.ReturnRequestId == ret1.ReturnRequestId);

        // SalesRep scope
        var repQuery = new ReturnRequestQuery { ScopeSalesRepId = rep1, Page = 1, PageSize = 10 };
        var (repItems, _) = await _repository.ListAsync(repQuery);
        repItems.Should().ContainSingle(x => x.ReturnRequestId == ret1.ReturnRequestId);

        // Agency scope
        var agencyQuery = new ReturnRequestQuery { ScopeAgencyId = agency2, Page = 1, PageSize = 10 };
        var (agencyItems, _) = await _repository.ListAsync(agencyQuery);
        agencyItems.Should().ContainSingle(x => x.ReturnRequestId == ret2.ReturnRequestId);

        // Province scope
        var provQuery = new ReturnRequestQuery { ScopeProvinceIds = [prov1], Page = 1, PageSize = 10 };
        var (provItems, _) = await _repository.ListAsync(provQuery);
        provItems.Should().HaveCount(2);

        // Empty scope
        var emptyQuery = new ReturnRequestQuery { Page = 1, PageSize = 10 };
        var (emptyItems, _) = await _repository.ListAsync(emptyQuery);
        emptyItems.Should().BeEmpty();
    }
}
