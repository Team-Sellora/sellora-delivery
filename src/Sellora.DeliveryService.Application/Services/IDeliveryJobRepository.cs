using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Application.Services;

public interface IDeliveryJobRepository
{
    Task AddAsync(DeliveryJob job, CancellationToken ct);
}
