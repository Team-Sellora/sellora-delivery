using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sellora.DeliveryService.Tests.Integration;

[Collection("PostgreSql")]
public class DeliveryJobQueryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("delivery_query_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private DeliveryDbContext _db = default!;
    private DeliveryJobRepository _repository = default!;
    private readonly Guid _tenantId = Guid.NewGuid();

    private class FakeTenantContext(Guid companyId) : Sellora.DeliveryService.Application.Common.Interfaces.ITenantContext
    {
        public Guid CompanyId { get; } = companyId;
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new DeliveryDbContext(options, new FakeTenantContext(_tenantId));
        await _db.Database.EnsureCreatedAsync();
        _repository = new DeliveryJobRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private DeliveryJob CreateJob(Guid agencyId, Guid shopId, string fulfilmentType)
    {
        var createMethod = typeof(DeliveryJob).GetMethod("CreatePending", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (createMethod != null)
        {
            return (DeliveryJob)createMethod.Invoke(null, new object[] {
                _tenantId, // companyId
                "DEL-123", // deliveryReference
                Guid.NewGuid(), // orderId
                "ORD-123",
                fulfilmentType,
                shopId,
                "Shop",
                "Owner",
                "email@test.com",
                agencyId,
                "Agency",
                "email@agency.com",
                Guid.NewGuid(), // territory
                Guid.NewGuid(), // province
                Guid.NewGuid(), // order rep
                100m, // total
                "LKR", // currency
                Array.Empty<DeliveryJobLine>() // lines
            })!;
        }
        
        var job = typeof(DeliveryJob).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new Type[0], null)?.Invoke(null) as DeliveryJob;
        
        typeof(DeliveryJob).GetProperty("DeliveryReference")?.SetValue(job, "DEL-123");
        typeof(DeliveryJob).GetProperty("AgencyId")?.SetValue(job, agencyId);
        typeof(DeliveryJob).GetProperty("ShopId")?.SetValue(job, shopId);
        typeof(DeliveryJob).GetProperty("FulfilmentType")?.SetValue(job, fulfilmentType);
        
        return job!;
    }

    [Fact]
    public async Task ListAsync_IncludeHandoversFalse_ExcludesImmediateCashSale()
    {
        var agencyId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        
        var job1 = CreateJob(agencyId, shopId, "ScheduledDelivery");
        var job2 = CreateJob(agencyId, shopId, "ImmediateCashSale");
        
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery
        {
            IsCompanyAdmin = true,
            IncludeHandovers = false,
            Page = 1,
            PageSize = 20
        };

        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items.Should().Contain(j => j.DeliveryJobId == job1.DeliveryJobId);
        items.Should().NotContain(j => j.DeliveryJobId == job2.DeliveryJobId);
    }

    [Fact]
    public async Task ListAsync_AgencyOperatorScope_ReturnsOnlyAgencyJobs()
    {
        var agency1 = Guid.NewGuid();
        var agency2 = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        
        var job1 = CreateJob(agency1, shopId, "ScheduledDelivery");
        var job2 = CreateJob(agency2, shopId, "ScheduledDelivery");
        
        _db.DeliveryJobs.AddRange(job1, job2);
        await _db.SaveChangesAsync();

        var query = new DeliveryJobQuery
        {
            IsCompanyAdmin = false,
            ScopeAgencyId = agency1,
            Page = 1,
            PageSize = 20
        };

        var (items, count) = await _repository.ListAsync(query);

        count.Should().Be(1);
        items.Should().Contain(j => j.DeliveryJobId == job1.DeliveryJobId);
    }
}
