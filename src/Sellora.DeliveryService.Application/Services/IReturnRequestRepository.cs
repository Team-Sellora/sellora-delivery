using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Application.Services;

public record ReturnRequestListItem(
    Guid ReturnRequestId,
    string ReturnReference,
    Guid DeliveryJobId,
    string OrderReference,
    string ShopName,
    ReturnStatus Status,
    DateTimeOffset RequestedAt,
    string RequestedByRole,
    int LineCount);

public class ReturnRequestQuery
{
    public List<string>? Statuses { get; init; }
    public Guid? ShopId { get; init; }
    public Guid? AgencyId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public bool IsCompanyAdmin { get; init; }
    public Guid? ScopeAgencyId { get; init; }
    public Guid? ScopeSalesRepId { get; init; }
    public Guid? ScopeShopId { get; init; }
    public IReadOnlyList<Guid>? ScopeProvinceIds { get; init; }
}

public interface IExecutionTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface IReturnRequestRepository
{
    Task<IExecutionTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task<DeliveryJob?> LockDeliveryJobAsync(Guid deliveryJobId, Guid companyId, CancellationToken ct = default);
    Task AddAsync(ReturnRequest returnRequest, CancellationToken ct = default);
    Task<ReturnRequest?> GetByIdAsync(Guid returnRequestId, CancellationToken ct = default);
    Task<ReturnRequest?> GetByIdWithLinesAsync(Guid returnRequestId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> GetReturnedQuantitiesByJobIdAsync(
        Guid deliveryJobId, Guid? excludeReturnRequestId = null, CancellationToken ct = default);
    Task<(IReadOnlyList<ReturnRequestListItem> Items, int TotalCount)> ListAsync(
        ReturnRequestQuery query, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
