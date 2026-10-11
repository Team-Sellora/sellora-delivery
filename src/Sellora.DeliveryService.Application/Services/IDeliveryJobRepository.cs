using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Application.Services;

public interface IDeliveryJobRepository
{
    Task AddAsync(DeliveryJob job, CancellationToken ct);
    Task<DeliveryJob?> GetByIdAsync(Guid deliveryJobId, CancellationToken ct = default);
    void SetOriginalVersion(DeliveryJob job, uint version);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<DeliveryJob?> GetByIdWithDetailsAsync(Guid deliveryJobId, CancellationToken ct = default);
    Task<(IReadOnlyList<DeliveryJob> Items, int TotalCount)> ListAsync(DeliveryJobQuery query, CancellationToken ct = default);
    Task AddConfirmationAsync(DeliveryConfirmation confirmation, CancellationToken ct = default);
    Task<DeliveryConfirmation?> GetConfirmationByJobIdAsync(Guid deliveryJobId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, DeliveryConfirmation>> GetConfirmationsByJobIdsAsync(IEnumerable<Guid> jobIds, CancellationToken ct = default);
}
