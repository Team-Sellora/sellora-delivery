using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Persistence;

namespace Sellora.DeliveryService.Infrastructure.Repositories;

internal class ReturnRequestRepository(DeliveryDbContext db) : IReturnRequestRepository
{
    public async Task<IExecutionTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        return new EfExecutionTransaction(tx);
    }

    private sealed class EfExecutionTransaction(IDbContextTransaction tx) : IExecutionTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => tx.CommitAsync(ct);
        public ValueTask DisposeAsync() => tx.DisposeAsync();
    }

    public async Task<DeliveryJob?> LockDeliveryJobAsync(Guid deliveryJobId, Guid companyId, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM delivery_jobs WHERE delivery_job_id = {deliveryJobId} AND company_id = {companyId} FOR UPDATE", ct);

        return await db.DeliveryJobs
            .AsSplitQuery()
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryJobId == deliveryJobId && x.CompanyId == companyId, ct);
    }

    public async Task AddAsync(ReturnRequest returnRequest, CancellationToken ct = default)
    {
        await db.ReturnRequests.AddAsync(returnRequest, ct);
    }

    public async Task<ReturnRequest?> GetByIdAsync(Guid returnRequestId, CancellationToken ct = default)
    {
        return await db.ReturnRequests.FirstOrDefaultAsync(r => r.ReturnRequestId == returnRequestId, ct);
    }

    public async Task<ReturnRequest?> GetByIdWithLinesAsync(Guid returnRequestId, CancellationToken ct = default)
    {
        return await db.ReturnRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.ReturnRequestId == returnRequestId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetReturnedQuantitiesByJobIdAsync(
        Guid deliveryJobId, Guid? excludeReturnRequestId = null, CancellationToken ct = default)
    {
        var query = db.ReturnRequests
            .Where(r => r.DeliveryJobId == deliveryJobId &&
                        (r.Status == ReturnStatus.Approved || r.Status == ReturnStatus.Requested));

        if (excludeReturnRequestId.HasValue)
        {
            query = query.Where(r => r.ReturnRequestId != excludeReturnRequestId.Value);
        }

        return await query
            .SelectMany(r => r.Lines)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct);
    }

    public async Task<(IReadOnlyList<ReturnRequestListItem> Items, int TotalCount)> ListAsync(
        ReturnRequestQuery query, CancellationToken ct = default)
    {
        var baseQuery = from r in db.ReturnRequests
                        join j in db.DeliveryJobs on r.DeliveryJobId equals j.DeliveryJobId
                        select new { Request = r, Job = j };

        if (!query.IsCompanyAdmin)
        {
            if (query.ScopeShopId.HasValue)
            {
                baseQuery = baseQuery.Where(x => x.Request.ShopId == query.ScopeShopId.Value);
            }
            else if (query.ScopeSalesRepId.HasValue)
            {
                baseQuery = baseQuery.Where(x => x.Job.AssignedRepId == query.ScopeSalesRepId.Value ||
                                                 x.Job.OrderSalesRepId == query.ScopeSalesRepId.Value);
            }
            else if (query.ScopeAgencyId.HasValue)
            {
                baseQuery = baseQuery.Where(x => x.Request.AgencyId == query.ScopeAgencyId.Value);
            }
            else if (query.ScopeProvinceIds != null && query.ScopeProvinceIds.Count > 0)
            {
                baseQuery = baseQuery.Where(x => query.ScopeProvinceIds.Contains(x.Request.ProvinceId));
            }
            else
            {
                baseQuery = baseQuery.Where(x => false);
            }
        }

        if (query.ShopId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Request.ShopId == query.ShopId.Value);
        }

        if (query.AgencyId.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Request.AgencyId == query.AgencyId.Value);
        }

        if (query.Statuses != null && query.Statuses.Count > 0)
        {
            var validStatuses = query.Statuses
                .Where(s => Enum.TryParse<ReturnStatus>(s, true, out _))
                .Select(s => Enum.Parse<ReturnStatus>(s, true))
                .ToList();

            if (validStatuses.Count > 0)
            {
                baseQuery = baseQuery.Where(x => validStatuses.Contains(x.Request.Status));
            }
        }

        var totalCount = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .OrderBy(x => x.Request.Status == ReturnStatus.Requested ? 0 : 1)
            .ThenByDescending(x => x.Request.RequestedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ReturnRequestListItem(
                x.Request.ReturnRequestId,
                x.Request.ReturnReference,
                x.Request.DeliveryJobId,
                x.Request.OrderReference,
                x.Job.ShopName,
                x.Request.Status,
                x.Request.RequestedAt,
                x.Request.RequestedByRole,
                x.Request.Lines.Count))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
