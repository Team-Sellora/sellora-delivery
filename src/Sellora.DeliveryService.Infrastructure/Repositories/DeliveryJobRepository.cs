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
}
