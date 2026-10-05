using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Application.Services;

public interface IDeliveryJobRepository
{
    Task AddAsync(DeliveryJob job, CancellationToken ct);
    Task<DeliveryJob?> GetByIdAsync(Guid deliveryJobId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
