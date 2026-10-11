using System.Net;
using Sellora.DeliveryService.Application.Dependencies;

namespace Sellora.DeliveryService.Infrastructure.Dependencies;

public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public async Task<IReadOnlyList<InventoryOwnerSummary>> GetInventoryOwnersAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/inventory-owners");

        using var response = await DependencyHttp.SendAsync(
            http, request, Dependency.Inventory, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Inventory,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        var result = await DependencyHttp.ReadAsync<List<InventoryOwnerSummary>>(
            response, Dependency.Inventory, cancellationToken);

        return result ?? [];
    }

    public async Task<InventoryOwnerSummary?> GetAgencyInventoryOwnerAsync(Guid agencyId, CancellationToken cancellationToken = default)
    {
        var owners = await GetInventoryOwnersAsync(cancellationToken);
        return owners.FirstOrDefault(o =>
            string.Equals(o.OwnerType, "Agency", StringComparison.OrdinalIgnoreCase) &&
            o.ExternalOwnerId == agencyId);
    }
}
