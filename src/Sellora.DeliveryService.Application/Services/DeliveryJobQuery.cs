namespace Sellora.DeliveryService.Application.Services;

public class DeliveryJobQuery
{
    public string[]? Statuses { get; set; }
    public DateOnly? ScheduledDateFrom { get; set; }
    public DateOnly? ScheduledDateTo { get; set; }
    public Guid? SalesRepId { get; set; }
    public Guid? ShopId { get; set; }
    public Guid? OrderId { get; set; }
    public bool IncludeHandovers { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public bool IsCompanyAdmin { get; set; }
    public Guid? ScopeAgencyId { get; set; }
    public Guid? ScopeSalesRepId { get; set; }
    public Guid? ScopeShopId { get; set; }
    public IReadOnlyList<Guid>? ScopeProvinceIds { get; set; }
}
