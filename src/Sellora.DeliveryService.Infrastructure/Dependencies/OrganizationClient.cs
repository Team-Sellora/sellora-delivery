using Sellora.DeliveryService.Application.Dependencies;

namespace Sellora.DeliveryService.Infrastructure.Dependencies;

public sealed class OrganizationClient : IOrganizationClient
{
    private readonly HttpClient _http;

    public OrganizationClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<SalesRepSummary>> ListSalesRepsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sales-reps");

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Organization, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Organization,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        var result = await DependencyHttp.ReadAsync<List<SalesRepSummary>>(
            response, Dependency.Organization, cancellationToken);
            
        return result ?? new List<SalesRepSummary>();
    }

    public async Task<CallerScope?> GetCallerScopeAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/me/scope");

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Organization, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Organization,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        var scope = await DependencyHttp.ReadAsync<CallerScopeDto>(
            response, Dependency.Organization, cancellationToken);

        return new CallerScope(
            scope.SalesRepId,
            scope.AgencyId,
            scope.ShopId,
            scope.ProvinceIds ?? new List<Guid>(),
            scope.DisplayName);
    }

    public async Task<ShopCoordinates?> GetShopCoordinatesAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/hierarchy");

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Organization, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Organization,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        var tree = await DependencyHttp.ReadAsync<HierarchyTreeResponse>(
            response, Dependency.Organization, cancellationToken);

        if (tree?.Provinces == null)
        {
            return null;
        }

        var shop = (
            from province in tree.Provinces
            where province.Agencies != null
            from agency in province.Agencies!
            where agency.Territories != null
            from territory in agency.Territories!
            where territory.Shops != null
            from s in territory.Shops!
            where s.ShopId == shopId
            select s
        ).FirstOrDefault();

        if (shop == null || shop.Latitude == null || shop.Longitude == null || (shop.Latitude == 0m && shop.Longitude == 0m))
        {
            return null;
        }

        return new ShopCoordinates(shop.Latitude.Value, shop.Longitude.Value);
    }

    private sealed record CallerScopeDto(
        Guid? SalesRepId,
        Guid? AgencyId,
        Guid? ShopId,
        List<Guid>? ProvinceIds,
        string? DisplayName);

    private sealed record HierarchyTreeResponse(
        Guid CompanyId,
        string? Name,
        List<ProvinceHierarchyNode>? Provinces);

    private sealed record ProvinceHierarchyNode(
        Guid ProvinceId,
        string? Code,
        string? Name,
        List<AgencyHierarchyNode>? Agencies);

    private sealed record AgencyHierarchyNode(
        Guid AgencyId,
        string? Name,
        List<TerritoryHierarchyNode>? Territories);

    private sealed record TerritoryHierarchyNode(
        Guid TerritoryId,
        string? Code,
        string? Name,
        List<ShopHierarchyNode>? Shops);

    private sealed record ShopHierarchyNode(
        Guid ShopId,
        decimal? Latitude,
        decimal? Longitude);
}
