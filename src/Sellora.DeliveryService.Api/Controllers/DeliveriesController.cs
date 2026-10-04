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
            return Conflict($"Delivery job is in {job.Status} status and cannot be assigned.");
        }

        var colomboZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        var todayColombo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, colomboZone).Date;
        var todayDateOnly = DateOnly.FromDateTime(todayColombo);
        
        if (request.ScheduledDate < todayDateOnly)
        {
            return BadRequest("Scheduled date cannot be in the past.");
        }

        var reps = await _organizationClient.ListSalesRepsAsync(ct);
        var eligibleRep = reps.FirstOrDefault(r => 
            r.Status == "Active" && 
            r.CurrentTerritory?.TerritoryId == job.TerritoryId && 
            r.SalesRepId == request.SalesRepId);

        if (eligibleRep == null)
        {
            return BadRequest($"Sales rep {request.SalesRepId} is not eligible for this delivery job. The rep must be active and assigned to territory {job.TerritoryId}.");
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
                deliveryJobId: job.DeliveryJobId,
                deliveryReference: job.DeliveryReference,
                orderId: job.OrderId,
                orderReference: job.OrderReference,
                fulfilmentType: job.FulfilmentType,
                agencyId: job.AgencyId,
                agencyName: job.AgencyName,
                shopId: job.ShopId,
                shopName: job.ShopName,
                status: job.Status.ToString(),
                assignedRepId: job.AssignedRepId,
                assignedRepName: job.AssignedRepName,
                scheduledDate: job.ScheduledDate,
                deliveredAt: job.DeliveredAt,
                occurredAt: DateTimeOffset.UtcNow
            ))
        );

        _outboxWriter.Write(outboxMessage);
        
        await _repository.SaveChangesAsync(ct);

        return NoContent();
    }
}

public class AssignDeliveryRequest
{
    public Guid SalesRepId { get; set; }
    public DateOnly ScheduledDate { get; set; }
}
