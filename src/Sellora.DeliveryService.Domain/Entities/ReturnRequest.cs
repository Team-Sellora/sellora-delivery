using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Domain.Entities;

public class ReturnRequest
{
    public Guid ReturnRequestId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ReturnReference { get; private set; } = default!;
    public Guid DeliveryJobId { get; private set; }
    public Guid OrderId { get; private set; }
    public string OrderReference { get; private set; } = default!;
    public Guid ShopId { get; private set; }
    public Guid AgencyId { get; private set; }
    public Guid TerritoryId { get; private set; }
    public Guid ProvinceId { get; private set; }
    public ReturnStatus Status { get; private set; }
    public string RequestedByUserId { get; private set; } = default!;
    public string RequestedByRole { get; private set; } = default!;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? DecidedByUserId { get; private set; }
    public string? DecidedByRole { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionReason { get; private set; }
    public Guid? InventoryOwnerId { get; private set; }

    public uint Version { get; private set; }

    private readonly List<ReturnRequestLine> _lines = [];
    public IReadOnlyCollection<ReturnRequestLine> Lines => _lines.AsReadOnly();

    private ReturnRequest() { }

    public static ReturnRequest Create(
        Guid companyId,
        string returnReference,
        Guid deliveryJobId,
        Guid orderId,
        string orderReference,
        Guid shopId,
        Guid agencyId,
        Guid territoryId,
        Guid provinceId,
        string requestedByUserId,
        string requestedByRole,
        DateTimeOffset requestedAt,
        IEnumerable<ReturnRequestLine> lines)
    {
        var request = new ReturnRequest
        {
            ReturnRequestId = Guid.NewGuid(),
            CompanyId = companyId,
            ReturnReference = returnReference,
            DeliveryJobId = deliveryJobId,
            OrderId = orderId,
            OrderReference = orderReference,
            ShopId = shopId,
            AgencyId = agencyId,
            TerritoryId = territoryId,
            ProvinceId = provinceId,
            Status = ReturnStatus.Requested,
            RequestedByUserId = requestedByUserId,
            RequestedByRole = requestedByRole,
            RequestedAt = requestedAt
        };

        request._lines.AddRange(lines);
        return request;
    }

    public void Approve(Guid inventoryOwnerId, string decidedByUserId, string decidedByRole, DateTimeOffset decidedAt)
    {
        if (Status != ReturnStatus.Requested)
        {
            throw new InvalidOperationException($"Cannot approve return request in status '{Status}'. Only Requested returns can be approved.");
        }

        Status = ReturnStatus.Approved;
        InventoryOwnerId = inventoryOwnerId;
        DecidedByUserId = decidedByUserId;
        DecidedByRole = decidedByRole;
        DecidedAt = decidedAt;
    }

    public void Reject(string reason, string decidedByUserId, string decidedByRole, DateTimeOffset decidedAt)
    {
        if (Status != ReturnStatus.Requested)
        {
            throw new InvalidOperationException($"Cannot reject return request in status '{Status}'. Only Requested returns can be rejected.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejection reason is required.", nameof(reason));
        }

        Status = ReturnStatus.Rejected;
        DecisionReason = reason.Trim();
        DecidedByUserId = decidedByUserId;
        DecidedByRole = decidedByRole;
        DecidedAt = decidedAt;
    }
}
