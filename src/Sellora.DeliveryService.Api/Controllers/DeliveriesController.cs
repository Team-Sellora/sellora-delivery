using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.DeliveryService.Api.Security;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Domain.Entities;
using System.Text.Json;
using Sellora.DeliveryService.Application.Events;

namespace Sellora.DeliveryService.Api.Controllers;

[ApiController]
[Route("api/deliveries")]
[Authorize]
public class DeliveriesController : ControllerBase
{
    private readonly IDeliveryJobRepository _repository;
    private readonly IOrganizationClient _organizationClient;
    private readonly ITenantContext _tenantContext;
    private readonly IOutboxWriter _outboxWriter;

    public DeliveriesController(
        IDeliveryJobRepository repository,
        IOrganizationClient organizationClient,
        ITenantContext tenantContext,
        IOutboxWriter outboxWriter)
    {
        _repository = repository;
        _organizationClient = organizationClient;
        _tenantContext = tenantContext;
        _outboxWriter = outboxWriter;
    }

    [HttpGet("{id:guid}/eligible-reps")]
    [Authorize(Policy = RolePolicies.AgencyRead)]
    public async Task<IActionResult> GetEligibleReps(Guid id, CancellationToken ct)
    {
        var job = await _repository.GetByIdAsync(id, ct);
        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);

        if (job == null || callerScope?.AgencyId == null || job.AgencyId != callerScope.AgencyId)
        {
            return NotFound();
        }

        var reps = await _organizationClient.ListSalesRepsAsync(ct);

        var eligibleReps = reps
            .Where(r => r.Status == "Active" && r.CurrentTerritory?.TerritoryId == job.TerritoryId)
            .ToList();

        return Ok(eligibleReps);
    }

    [HttpPut("{id:guid}/assignment")]
    [Authorize(Policy = RolePolicies.AgencyWrite)]
    public async Task<IActionResult> AssignDeliveryJob(Guid id, [FromBody] AssignDeliveryRequest request, CancellationToken ct)
    {
        var job = await _repository.GetByIdAsync(id, ct);
        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        
        if (job == null || callerScope?.AgencyId == null || job.AgencyId != callerScope.AgencyId)
        {
            return NotFound();
        }

        if (job.Status != DeliveryStatus.Pending)
        {
            return Conflict(new { detail = $"Delivery job is in {job.Status} status and cannot be assigned." });
        }

        var colomboZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        var todayColombo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, colomboZone).Date;
        var todayDateOnly = DateOnly.FromDateTime(todayColombo);
        
        if (request.ScheduledDate < todayDateOnly)
        {
            return BadRequest(new { detail = "Scheduled date cannot be in the past." });
        }

        var reps = await _organizationClient.ListSalesRepsAsync(ct);
        var eligibleRep = reps.FirstOrDefault(r => 
            r.Status == "Active" && 
            r.CurrentTerritory?.TerritoryId == job.TerritoryId && 
            r.SalesRepId == request.SalesRepId);

        if (eligibleRep == null)
        {
            return BadRequest(new { detail = $"Sales rep {request.SalesRepId} is not eligible for this delivery job. The rep must be active and assigned to territory {job.TerritoryId}." });
        }

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var role = User.FindFirst("roles")?.Value ?? "AgencyOperator";
        
        job.Assign(
            eligibleRep.SalesRepId, 
            eligibleRep.DisplayName, 
            request.ScheduledDate, 
            userId, 
            role);

        var outboxMessage = new OutboxMessage(
            job.CompanyId,
            "sellora.delivery.v1",
            job.OrderReference,
            "DeliveryStatusChanged",
            JsonSerializer.Serialize(DeliveryStatusChangedEvent.Create(
                companyId: job.CompanyId,
                deliveryId: job.DeliveryJobId,
                orderId: job.OrderId,
                orderReference: job.OrderReference,
                previousStatus: DeliveryStatus.Pending.ToString(),
                status: job.Status.ToString(),
                occurredAt: DateTimeOffset.UtcNow,
                reason: $"Assigned to {eligibleRep.DisplayName} for {request.ScheduledDate:yyyy-MM-dd}",
                scheduledFor: job.ScheduledDate,
                shopId: job.ShopId,
                shopName: job.ShopName,
                shopOwnerName: job.ShopOwnerName,
                shopOwnerEmail: job.ShopOwnerEmail,
                agencyId: job.AgencyId,
                agencyName: job.AgencyName,
                agencyEmail: job.AgencyEmail,
                deliveryReference: job.DeliveryReference,
                territoryId: job.TerritoryId,
                provinceId: job.ProvinceId,
                actorUserId: userId,
                actorRole: role
            ))
        );

        _outboxWriter.Write(outboxMessage);
        
        await _repository.SaveChangesAsync(ct);

        return NoContent();
    }
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = RolePolicies.RepWrite)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDeliveryStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<DeliveryStatus>(request.Status, out var newStatus) ||
            (newStatus != DeliveryStatus.InTransit && newStatus != DeliveryStatus.Delivered && newStatus != DeliveryStatus.Failed))
        {
            return BadRequest(new { detail = "Status must be InTransit, Delivered, or Failed." });
        }

        if (newStatus == DeliveryStatus.Failed && string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { detail = "A reason is required when marking a delivery as Failed." });
        }
        
        if (request.Reason?.Length > 500)
        {
            return BadRequest(new { detail = "Reason cannot exceed 500 characters." });
        }

        var job = await _repository.GetByIdAsync(id, ct);
        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);

        if (job == null || callerScope?.SalesRepId == null)
        {
            return NotFound();
        }

        if (job.AssignedRepId != callerScope.SalesRepId)
        {
            return Forbid();
        }

        if (!DeliveryTransitions.IsAllowed(job.Status, newStatus))
        {
            return Conflict(new { detail = $"Transition from {job.Status} to {newStatus} is not allowed." });
        }

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var role = User.FindFirst("roles")?.Value ?? "SalesRep";

        job.ChangeStatus(newStatus, userId, role, request.Reason);
        _repository.SetOriginalVersion(job, request.Version);

        var outboxMessage = new OutboxMessage(
            job.CompanyId,
            "sellora.delivery.v1",
            job.OrderReference,
            "DeliveryStatusChanged",
            JsonSerializer.Serialize(DeliveryStatusChangedEvent.Create(
                companyId: job.CompanyId,
                deliveryId: job.DeliveryJobId,
                orderId: job.OrderId,
                orderReference: job.OrderReference,
                previousStatus: job.StatusHistory.OrderByDescending(h => h.OccurredAt).Skip(1).FirstOrDefault()?.ToStatus.ToString() ?? DeliveryStatus.Pending.ToString(),
                status: newStatus.ToString(),
                occurredAt: DateTimeOffset.UtcNow,
                reason: request.Reason,
                scheduledFor: job.ScheduledDate,
                shopId: job.ShopId,
                shopName: job.ShopName,
                shopOwnerName: job.ShopOwnerName,
                shopOwnerEmail: job.ShopOwnerEmail,
                agencyId: job.AgencyId,
                agencyName: job.AgencyName,
                agencyEmail: job.AgencyEmail,
                deliveryReference: job.DeliveryReference,
                territoryId: job.TerritoryId,
                provinceId: job.ProvinceId,
                actorUserId: userId,
                actorRole: role
            ))
        );

        _outboxWriter.Write(outboxMessage);

        try
        {
            await _repository.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return StatusCode(412, new { detail = "Concurrency conflict. The delivery job has been updated by another process." });
        }

        return NoContent();
    }
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetDeliveries(
        [FromQuery] string[]? status,
        [FromQuery] DateOnly? scheduledDateFrom,
        [FromQuery] DateOnly? scheduledDateTo,
        [FromQuery] Guid? salesRepId,
        [FromQuery] Guid? shopId,
        [FromQuery] Guid? orderId,
        [FromQuery] bool includeHandovers = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        var isCompanyAdmin = User.IsInRole(SelloraRoles.SystemAdmin) || User.IsInRole(SelloraRoles.CompanyAdmin);
        var isAreaManager = User.IsInRole(SelloraRoles.AreaManager);
        var isAgencyOperator = User.IsInRole(SelloraRoles.AgencyOperator);
        var isSalesRep = User.IsInRole(SelloraRoles.SalesRep);
        var isShopOwner = User.IsInRole(SelloraRoles.ShopOwner);

        if (!isCompanyAdmin)
        {
            if (isShopOwner && shopId.HasValue && shopId.Value != callerScope?.ShopId)
            {
                return Forbid();
            }

            if (isSalesRep && salesRepId.HasValue && salesRepId.Value != callerScope?.SalesRepId)
            {
                return Forbid();
            }
        }

        var query = new DeliveryJobQuery
        {
            Statuses = status,
            ScheduledDateFrom = scheduledDateFrom,
            ScheduledDateTo = scheduledDateTo,
            SalesRepId = salesRepId,
            ShopId = shopId,
            OrderId = orderId,
            IncludeHandovers = includeHandovers,
            Page = page,
            PageSize = pageSize,
            IsCompanyAdmin = isCompanyAdmin,
            ScopeAgencyId = isAgencyOperator ? callerScope?.AgencyId : null,
            ScopeSalesRepId = isSalesRep ? callerScope?.SalesRepId : null,
            ScopeShopId = isShopOwner ? callerScope?.ShopId : null,
            ScopeProvinceIds = isAreaManager ? callerScope?.ProvinceIds : null
        };

        var (items, totalCount) = await _repository.ListAsync(query, ct);

        var dtos = items.Select(j => new DeliveryListItemDto(
            j.DeliveryJobId,
            j.DeliveryReference,
            j.OrderReference,
            j.ShopName,
            j.TerritoryId,
            j.Status.ToString(),
            j.ScheduledDate,
            j.AssignedRepName,
            j.AgencyName)).ToList();

        return Ok(new DeliveryListResponse(dtos, page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetDeliveryById(Guid id, CancellationToken ct)
    {
        var job = await _repository.GetByIdWithDetailsAsync(id, ct);
        if (job == null)
        {
            return NotFound();
        }

        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        var isCompanyAdmin = User.IsInRole(SelloraRoles.SystemAdmin) || User.IsInRole(SelloraRoles.CompanyAdmin);
        
        if (!isCompanyAdmin)
        {
            if (User.IsInRole(SelloraRoles.ShopOwner) && job.ShopId != callerScope?.ShopId)
                return NotFound();
            
            if (User.IsInRole(SelloraRoles.SalesRep) && job.AssignedRepId != callerScope?.SalesRepId)
                return NotFound();
            
            if (User.IsInRole(SelloraRoles.AgencyOperator) && job.AgencyId != callerScope?.AgencyId)
                return NotFound();
            
            if (User.IsInRole(SelloraRoles.AreaManager) && (callerScope?.ProvinceIds == null || !callerScope.ProvinceIds.Contains(job.ProvinceId)))
                return NotFound();
        }

        // Ordered oldest-first for a typical timeline UI
        var history = job.StatusHistory
            .OrderBy(h => h.OccurredAt)
            .Select(h => new DeliveryStatusHistoryDto(
                h.ToStatus.ToString(),
                h.ActorRole,
                h.OccurredAt,
                h.Reason))
            .ToList();

        var lines = job.Lines.Select(l => new DeliveryLineDto(
            l.DeliveryJobLineId,
            l.ProductId,
            l.ProductName,
            l.Quantity,
            l.UnitPrice,
            l.LineTotal,
            l.Quantity // Placeholder
        )).ToList();

        var dto = new DeliveryDetailResponse(
            job.DeliveryJobId,
            job.DeliveryReference,
            job.OrderId,
            job.OrderReference,
            job.FulfilmentType,
            job.ShopId,
            job.ShopName,
            job.ShopOwnerName,
            job.ShopOwnerEmail,
            job.AgencyId,
            job.AgencyName,
            job.AgencyEmail,
            job.TerritoryId,
            job.ProvinceId,
            job.Total,
            job.Currency,
            job.Status.ToString(),
            job.AssignedRepId,
            job.AssignedRepName,
            job.ScheduledDate,
            job.DeliveredAt,
            job.CreatedAt,
            job.Version,
            lines,
            history,
            null // Placeholder
        );

        return Ok(dto);
    }
}

public class AssignDeliveryRequest
{
    public Guid SalesRepId { get; set; }
    public DateOnly ScheduledDate { get; set; }
}

public class UpdateDeliveryStatusRequest
{
    public string Status { get; set; } = default!;
    public string? Reason { get; set; }
    public uint Version { get; set; }
}

public record DeliveryListResponse(
    IReadOnlyList<DeliveryListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record DeliveryListItemDto(
    Guid Id,
    string DeliveryReference,
    string OrderReference,
    string ShopName,
    Guid TerritoryId,
    string Status,
    DateOnly? ScheduledDate,
    string? AssignedRepName,
    string AgencyName);

public record DeliveryDetailResponse(
    Guid Id,
    string DeliveryReference,
    Guid OrderId,
    string OrderReference,
    string FulfilmentType,
    Guid ShopId,
    string ShopName,
    string ShopOwnerName,
    string ShopOwnerEmail,
    Guid AgencyId,
    string AgencyName,
    string AgencyEmail,
    Guid TerritoryId,
    Guid ProvinceId,
    decimal Total,
    string Currency,
    string Status,
    Guid? AssignedRepId,
    string? AssignedRepName,
    DateOnly? ScheduledDate,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset CreatedAt,
    uint Version,
    IReadOnlyList<DeliveryLineDto> Lines,
    IReadOnlyList<DeliveryStatusHistoryDto> History,
    object? Confirmation
);

public record DeliveryLineDto(
    Guid LineId,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int ReturnableQuantity
);

public record DeliveryStatusHistoryDto(
    string status,
    string actorRole,
    DateTimeOffset occurredAt,
    string? reason
);
