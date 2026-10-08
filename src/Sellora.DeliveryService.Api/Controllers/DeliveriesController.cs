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
