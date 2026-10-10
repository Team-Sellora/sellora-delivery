using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sellora.DeliveryService.Infrastructure.Repositories;

internal class DeliveryJobRepository(DeliveryDbContext db) : IDeliveryJobRepository
{
    public async Task AddAsync(DeliveryJob job, CancellationToken ct)
    {
        await db.DeliveryJobs.AddAsync(job, ct);
    }

    public async Task<DeliveryJob?> GetByIdAsync(Guid deliveryJobId, CancellationToken ct = default)
    {
        return await db.DeliveryJobs.FirstOrDefaultAsync(x => x.DeliveryJobId == deliveryJobId, ct);
    }

    public void SetOriginalVersion(DeliveryJob job, uint version)
    {
        db.Entry(job).Property(j => j.Version).OriginalValue = version;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeliveryJob?> GetByIdWithDetailsAsync(Guid deliveryJobId, CancellationToken ct = default)
    {
        return await db.DeliveryJobs
            .AsSplitQuery()
            .Include(j => j.Lines)
            .Include(j => j.StatusHistory)
            .FirstOrDefaultAsync(x => x.DeliveryJobId == deliveryJobId, ct);
    }

    public async Task<(IReadOnlyList<DeliveryJob> Items, int TotalCount)> ListAsync(DeliveryJobQuery query, CancellationToken ct = default)
    {
        var q = db.DeliveryJobs.AsQueryable();

        q = ApplyScopeFilters(q, query);
        q = ApplyQueryFilters(q, query);

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .OrderBy(j => j.ScheduledDate.HasValue ? 0 : 1)
            .ThenBy(j => j.ScheduledDate)
            .ThenBy(j => j.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    private static IQueryable<DeliveryJob> ApplyScopeFilters(IQueryable<DeliveryJob> q, DeliveryJobQuery query)
    {
        if (query.IsCompanyAdmin)
        {
            return q;
        }

        if (query.ScopeShopId.HasValue)
        {
            return q.Where(j => j.ShopId == query.ScopeShopId.Value);
        }

        if (query.ScopeSalesRepId.HasValue)
        {
            return q.Where(j => j.AssignedRepId == query.ScopeSalesRepId.Value);
        }

        if (query.ScopeAgencyId.HasValue)
        {
            return q.Where(j => j.AgencyId == query.ScopeAgencyId.Value);
        }

        if (query.ScopeProvinceIds != null && query.ScopeProvinceIds.Any())
        {
            return q.Where(j => query.ScopeProvinceIds.Contains(j.ProvinceId));
        }

        return q.Where(j => false);
    }

    private static IQueryable<DeliveryJob> ApplyQueryFilters(IQueryable<DeliveryJob> q, DeliveryJobQuery query)
    {
        if (query.Statuses != null && query.Statuses.Any())
        {
            var validStatuses = query.Statuses
                .Where(s => Enum.TryParse<Domain.Enums.DeliveryStatus>(s, out _))
                .Select(Enum.Parse<Domain.Enums.DeliveryStatus>)
                .ToList();
            if (validStatuses.Any())
            {
                q = q.Where(j => validStatuses.Contains(j.Status));
            }
        }

        if (query.ScheduledDateFrom.HasValue)
        {
            q = q.Where(j => j.ScheduledDate >= query.ScheduledDateFrom.Value);
        }

        if (query.ScheduledDateTo.HasValue)
        {
            q = q.Where(j => j.ScheduledDate <= query.ScheduledDateTo.Value);
        }

        if (query.SalesRepId.HasValue)
        {
            q = q.Where(j => j.AssignedRepId == query.SalesRepId.Value);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(j => j.ShopId == query.ShopId.Value);
        }

        if (query.OrderId.HasValue)
        {
            q = q.Where(j => j.OrderId == query.OrderId.Value);
        }

        if (!query.IncludeHandovers)
        {
            q = q.Where(j => j.FulfilmentType != "ImmediateCashSale");
        }

        return q;
    }

    public async Task AddConfirmationAsync(DeliveryConfirmation confirmation, CancellationToken ct = default)
    {
        await db.DeliveryConfirmations.AddAsync(confirmation, ct);
    }

    public async Task<DeliveryConfirmation?> GetConfirmationByJobIdAsync(Guid deliveryJobId, CancellationToken ct = default)
    {
        return await db.DeliveryConfirmations.FirstOrDefaultAsync(c => c.DeliveryJobId == deliveryJobId, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, DeliveryConfirmation>> GetConfirmationsByJobIdsAsync(IEnumerable<Guid> jobIds, CancellationToken ct = default)
    {
        var list = await db.DeliveryConfirmations
            .Where(c => jobIds.Contains(c.DeliveryJobId))
            .ToListAsync(ct);

        return list.ToDictionary(c => c.DeliveryJobId);
    }
}
