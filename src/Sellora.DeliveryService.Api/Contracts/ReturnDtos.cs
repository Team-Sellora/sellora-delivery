using System.ComponentModel.DataAnnotations;

namespace Sellora.DeliveryService.Api.Contracts;

public class CreateReturnRequest
{
    [Required]
    public Guid DeliveryJobId { get; set; }

    [Required]
    public List<CreateReturnRequestLine> Lines { get; set; } = [];
}

public class CreateReturnRequestLine
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = default!;
}

public record CreateReturnResponse(
    Guid ReturnRequestId,
    string ReturnReference,
    string Status);

public class DecideReturnRequest
{
    [Required]
    public string Decision { get; set; } = default!;

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class ReturnListFilter
{
    public List<string>? Status { get; set; }
    public Guid? ShopId { get; set; }
    public Guid? AgencyId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record ReturnListItemDto(
    Guid ReturnRequestId,
    string ReturnReference,
    Guid DeliveryJobId,
    string OrderReference,
    string ShopName,
    string Status,
    DateTimeOffset RequestedAt,
    string RequestedByRole,
    int LineCount);

public record ReturnListResponse(
    IReadOnlyList<ReturnListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record ReturnLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    string Reason);

public record ReturnDetailResponse(
    Guid ReturnRequestId,
    string ReturnReference,
    Guid DeliveryJobId,
    Guid OrderId,
    string OrderReference,
    Guid ShopId,
    string ShopName,
    Guid AgencyId,
    string AgencyName,
    Guid TerritoryId,
    Guid ProvinceId,
    string Status,
    string RequestedByUserId,
    string RequestedByRole,
    DateTimeOffset RequestedAt,
    string? DecidedByUserId,
    string? DecidedByRole,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    Guid? InventoryOwnerId,
    IReadOnlyList<ReturnLineDto> Lines);
