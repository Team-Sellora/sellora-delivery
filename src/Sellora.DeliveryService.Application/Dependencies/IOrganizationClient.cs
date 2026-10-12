namespace Sellora.DeliveryService.Application.Dependencies;

public record SalesRepSummary(
    Guid SalesRepId,
    string DisplayName,
    string? Email,
    string Status,
    TerritoryReference CurrentTerritory);

public record TerritoryReference(
    Guid TerritoryId,
    string Code,
    string Name);

public record CallerScope(
    Guid? SalesRepId,
    Guid? AgencyId,
    Guid? ShopId,
    IReadOnlyList<Guid> ProvinceIds,
    string? DisplayName);

public record ShopCoordinates(
    decimal Latitude,
    decimal Longitude);

public interface IOrganizationClient
{
    Task<IReadOnlyList<SalesRepSummary>> ListSalesRepsAsync(CancellationToken cancellationToken = default);
    Task<CallerScope?> GetCallerScopeAsync(CancellationToken cancellationToken = default);
    Task<ShopCoordinates?> GetShopCoordinatesAsync(Guid shopId, CancellationToken cancellationToken = default);
}
