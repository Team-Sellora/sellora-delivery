using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Sellora.DeliveryService.Api.Contracts;

[ExcludeFromCodeCoverage]
public class CreateReturnRequest
{
    [Required]
    [JsonRequired]
    public Guid DeliveryJobId { get; set; }

    [Required]
    public List<CreateReturnRequestLine> Lines { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public class CreateReturnRequestLine
{
    [Required]
    [JsonRequired]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    [JsonRequired]
    public int Quantity { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = default!;
}

[ExcludeFromCodeCoverage]
public record CreateReturnResponse(
    Guid ReturnRequestId,
    string ReturnReference,
    string Status);

[ExcludeFromCodeCoverage]
public class DecideReturnRequest
{
    [Required]
    public string Decision { get; set; } = default!;

    [MaxLength(500)]
    public string? Reason { get; set; }
}

[ExcludeFromCodeCoverage]
public class ReturnListFilter
{
    public List<string>? Status { get; set; }
    public Guid? ShopId { get; set; }
    public Guid? AgencyId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

[ExcludeFromCodeCoverage]
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

[ExcludeFromCodeCoverage]
public record ReturnListResponse(
    IReadOnlyList<ReturnListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

[ExcludeFromCodeCoverage]
public record ReturnLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    string Reason);

[ExcludeFromCodeCoverage]
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
