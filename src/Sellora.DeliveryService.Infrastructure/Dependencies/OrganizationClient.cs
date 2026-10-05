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

    private sealed record CallerScopeDto(
        Guid? SalesRepId,
        Guid? AgencyId,
        Guid? ShopId,
        List<Guid>? ProvinceIds,
        string? DisplayName);
}
