using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Persistence;

namespace Sellora.DeliveryService.Infrastructure.Repositories;

internal class DeliveryJobRepository(DeliveryDbContext db) : IDeliveryJobRepository
{
    public async Task AddAsync(DeliveryJob job, CancellationToken ct)
    {
        await db.DeliveryJobs.AddAsync(job, ct);
    }
}
