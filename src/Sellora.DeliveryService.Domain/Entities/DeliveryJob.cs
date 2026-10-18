using System.Diagnostics.CodeAnalysis;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Domain.Entities;

public class DeliveryJob
{
    public Guid DeliveryJobId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string DeliveryReference { get; private set; } = default!;
    public Guid OrderId { get; private set; }
    public string OrderReference { get; private set; } = default!;
    public string FulfilmentType { get; private set; } = default!;

    public Guid ShopId { get; private set; }
    public string ShopName { get; private set; } = default!;
    public string ShopOwnerName { get; private set; } = default!;
    public string ShopOwnerEmail { get; private set; } = default!;

    public Guid AgencyId { get; private set; }
    public string AgencyName { get; private set; } = default!;
    public string AgencyEmail { get; private set; } = default!;

    public Guid TerritoryId { get; private set; }
    public Guid ProvinceId { get; private set; }
    public Guid OrderSalesRepId { get; private set; }

    public decimal Total { get; private set; }
    public string Currency { get; private set; } = default!;

    public DeliveryStatus Status { get; private set; }
    public Guid? AssignedRepId { get; private set; }
    public string? AssignedRepName { get; private set; }
    public DateOnly? ScheduledDate { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public decimal? ShopLatitude { get; private set; }
    public decimal? ShopLongitude { get; private set; }

    public decimal? LastLatitude { get; private set; }
    public decimal? LastLongitude { get; private set; }
    public DateTimeOffset? LastLocationAt { get; private set; }

    // EF Core xmin optimistic concurrency
    [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Set by EF Core from the PostgreSQL xmin concurrency token.")]
    public uint Version { get; private set; }

    private readonly List<DeliveryJobLine> _lines = [];
    public IReadOnlyCollection<DeliveryJobLine> Lines => _lines.AsReadOnly();

    private readonly List<DeliveryStatusHistory> _statusHistory = [];
    public IReadOnlyCollection<DeliveryStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    private DeliveryJob() { }

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Domain factory takes every field of the entity snapshot; callers use named arguments.")]
    public static DeliveryJob CreatePending(
        Guid companyId,
        string deliveryReference,
        Guid orderId,
        string orderReference,
        string fulfilmentType,
        Guid shopId,
        string shopName,
        string shopOwnerName,
        string shopOwnerEmail,
        Guid agencyId,
        string agencyName,
        string agencyEmail,
        Guid territoryId,
        Guid provinceId,
        Guid orderSalesRepId,
        decimal total,
        string currency,
        IEnumerable<DeliveryJobLine> lines)
    {
        var job = new DeliveryJob
        {
            DeliveryJobId = Guid.NewGuid(),
            CompanyId = companyId,
            DeliveryReference = deliveryReference,
            OrderId = orderId,
            OrderReference = orderReference,
            FulfilmentType = fulfilmentType,
            ShopId = shopId,
            ShopName = shopName,
            ShopOwnerName = shopOwnerName,
            ShopOwnerEmail = shopOwnerEmail,
            AgencyId = agencyId,
            AgencyName = agencyName,
            AgencyEmail = agencyEmail,
            TerritoryId = territoryId,
            ProvinceId = provinceId,
            OrderSalesRepId = orderSalesRepId,
            Total = total,
            Currency = currency,
            Status = DeliveryStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        job._lines.AddRange(lines);

        return job;
    }

    // ImmediateCashSale orders bypass the pending/assigned/transit lifecycle — the goods
    // already left the van at checkout, so the record is created in its terminal state.
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Domain factory takes every field of the entity snapshot; callers use named arguments.")]
    public static DeliveryJob CreateDelivered(
        Guid companyId,
        string deliveryReference,
        Guid orderId,
        string orderReference,
        string fulfilmentType,
        Guid shopId,
        string shopName,
        string shopOwnerName,
        string shopOwnerEmail,
        Guid agencyId,
        string agencyName,
        string agencyEmail,
        Guid territoryId,
        Guid provinceId,
        Guid orderSalesRepId,
        decimal total,
        string currency,
        DateTimeOffset deliveredAt,
        IEnumerable<DeliveryJobLine> lines,
        string actorUserId)
    {
        var job = new DeliveryJob
        {
            DeliveryJobId = Guid.NewGuid(),
            CompanyId = companyId,
            DeliveryReference = deliveryReference,
            OrderId = orderId,
            OrderReference = orderReference,
            FulfilmentType = fulfilmentType,
            ShopId = shopId,
            ShopName = shopName,
            ShopOwnerName = shopOwnerName,
            ShopOwnerEmail = shopOwnerEmail,
            AgencyId = agencyId,
            AgencyName = agencyName,
            AgencyEmail = agencyEmail,
            TerritoryId = territoryId,
            ProvinceId = provinceId,
            OrderSalesRepId = orderSalesRepId,
            Total = total,
            Currency = currency,
            Status = DeliveryStatus.Delivered,
            DeliveredAt = deliveredAt,
            CreatedAt = DateTimeOffset.UtcNow
        };

        job._lines.AddRange(lines);

        job._statusHistory.Add(new DeliveryStatusHistory(
            job.DeliveryJobId,
            job.CompanyId,
            fromStatus: null,
            toStatus: DeliveryStatus.Delivered,
            actorUserId: actorUserId,
            actorRole: "System",
            reason: "ImmediateCashSale fulfilled at checkout",
            occurredAt: deliveredAt));

        return job;
    }

    public void Assign(
        Guid salesRepId,
        string salesRepName,
        DateOnly scheduledDate,
        string actorUserId,
        string actorRole,
        decimal? shopLatitude = null,
        decimal? shopLongitude = null)
    {
        var oldStatus = Status;
        Status = DeliveryStatus.Assigned;
        AssignedRepId = salesRepId;
        AssignedRepName = salesRepName;
        ScheduledDate = scheduledDate;
        ShopLatitude = shopLatitude;
        ShopLongitude = shopLongitude;

        _statusHistory.Add(new DeliveryStatusHistory(
            DeliveryJobId,
            CompanyId,
            fromStatus: oldStatus,
            toStatus: DeliveryStatus.Assigned,
            actorUserId: actorUserId,
            actorRole: actorRole,
            reason: $"Assigned to {salesRepName} for {scheduledDate:yyyy-MM-dd}",
            occurredAt: DateTimeOffset.UtcNow));
    }

    public void SetShopCoordinates(decimal? latitude, decimal? longitude)
    {
        ShopLatitude = latitude;
        ShopLongitude = longitude;
    }

    public void UpdateLatestLocation(decimal latitude, decimal longitude, DateTimeOffset capturedAt)
    {
        LastLatitude = latitude;
        LastLongitude = longitude;
        LastLocationAt = capturedAt;
    }

    public void ChangeStatus(
        DeliveryStatus newStatus,
        string actorUserId,
        string actorRole,
        string? reason = null)
    {
        if (!DeliveryTransitions.IsAllowed(Status, newStatus))
        {
            throw new InvalidOperationException($"Transition from {Status} to {newStatus} is not allowed.");
        }

        var oldStatus = Status;
        Status = newStatus;
        
        var occurredAt = DateTimeOffset.UtcNow;

        if (newStatus == DeliveryStatus.Delivered)
        {
            DeliveredAt = occurredAt;
        }

        _statusHistory.Add(new DeliveryStatusHistory(
            DeliveryJobId,
            CompanyId,
            fromStatus: oldStatus,
            toStatus: newStatus,
            actorUserId: actorUserId,
            actorRole: actorRole,
            reason: reason,
            occurredAt: occurredAt));
    }
}
