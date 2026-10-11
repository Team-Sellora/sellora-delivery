namespace Sellora.DeliveryService.Application.Dependencies;

public record InventoryOwnerSummary(
    Guid InventoryOwnerId,
    string OwnerType,
    Guid ExternalOwnerId,
    string DisplayName);

public interface IInventoryClient
{
    Task<IReadOnlyList<InventoryOwnerSummary>> GetInventoryOwnersAsync(CancellationToken cancellationToken = default);
    Task<InventoryOwnerSummary?> GetAgencyInventoryOwnerAsync(Guid agencyId, CancellationToken cancellationToken = default);
}
