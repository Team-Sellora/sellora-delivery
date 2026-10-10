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
using System.Diagnostics.CodeAnalysis;

namespace Sellora.DeliveryService.Api.Controllers;

[ApiController]
[Route("api/deliveries")]
[Authorize]
public class DeliveriesController : ControllerBase
{
    private readonly IDeliveryJobRepository _repository;
    private readonly IOrganizationClient _organizationClient;
    private readonly IOutboxWriter _outboxWriter;

    public DeliveriesController(
        IDeliveryJobRepository repository,
        IOrganizationClient organizationClient,
        IOutboxWriter outboxWriter)
    {
        _repository = repository;
        _organizationClient = organizationClient;
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
        

        var roles = GetUserRoles();
        var isCompanyAdmin = roles.Contains(SelloraRoles.SystemAdmin) || roles.Contains(SelloraRoles.CompanyAdmin);
        var isAreaManager = roles.Contains(SelloraRoles.AreaManager);
        var isAgencyOperator = roles.Contains(SelloraRoles.AgencyOperator);
        var isSalesRep = roles.Contains(SelloraRoles.SalesRep);
        var isShopOwner = roles.Contains(SelloraRoles.ShopOwner);

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

        IReadOnlyDictionary<Guid, DeliveryConfirmation> confirmations = new Dictionary<Guid, DeliveryConfirmation>();
        if (items.Count > 0)
        {
            var jobIds = items.Select(j => j.DeliveryJobId).ToList();
            confirmations = await _repository.GetConfirmationsByJobIdsAsync(jobIds, ct)
                ?? new Dictionary<Guid, DeliveryConfirmation>();
        }

        var dtos = items.Select(j =>
        {
            string? confirmationState = null;
            if (j.FulfilmentType != "ImmediateCashSale")
            {
                if (confirmations.TryGetValue(j.DeliveryJobId, out var conf))
                {
                    confirmationState = conf.Type.ToString();
                }
                else if (j.Status == DeliveryStatus.Delivered)
                {
                    confirmationState = "Awaiting";
                }
            }

            return new DeliveryListItemDto(
                j.DeliveryJobId,
                j.DeliveryReference,
                j.OrderReference,
                j.ShopName,
                j.TerritoryId,
                j.Status.ToString(),
                j.ScheduledDate,
                j.AssignedRepName,
                j.AgencyName,
                confirmationState);
        }).ToList();

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
        var accessError = ValidateDeliveryAccess(job, callerScope, GetUserRoles());
        if (accessError != null)
        {
            return accessError;
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

        var confirmation = await _repository.GetConfirmationByJobIdAsync(id, ct);
        var confirmationState = ResolveConfirmationState(job, confirmation);

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
            confirmationState
        );

        return Ok(dto);
    }

    [HttpPost("{id:guid}/confirmation")]
    [Authorize]
    public async Task<IActionResult> ConfirmDelivery(Guid id, [FromBody] DeliveryConfirmationRequest request, CancellationToken ct)
    {
        var roles = GetUserRoles();
        if (!roles.Contains(SelloraRoles.ShopOwner) && !roles.Contains(SelloraRoles.SystemAdmin))
        {
            return Forbid();
        }

        var job = await _repository.GetByIdAsync(id, ct);
        if (job == null)
        {
            return NotFound();
        }

        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        var eligibilityError = ValidateJobEligibility(job, callerScope);
        if (eligibilityError != null)
        {
            return eligibilityError;
        }

        var payloadError = ValidateRequestPayload(request, out var confirmationType, out var notes);
        if (payloadError != null)
        {
            return payloadError;
        }

        var existingConfirmation = await _repository.GetConfirmationByJobIdAsync(id, ct);
        if (existingConfirmation != null)
        {
            return HandleExistingConfirmation(existingConfirmation, confirmationType);
        }

        var confirmation = DeliveryConfirmation.Create(
            job.DeliveryJobId,
            job.CompanyId,
            confirmationType,
            notes,
            request.SignatureUrl
        );

        await _repository.AddConfirmationAsync(confirmation, ct);

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var role = roles.FirstOrDefault() ?? "ShopOwner";
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var outboxMessage = CreateConfirmationOutboxMessage(job, confirmation, userId, role, correlationId);
        _outboxWriter.Write(outboxMessage);

        try
        {
            await _repository.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return Conflict(new { detail = "A confirmation already exists for this delivery." });
        }

        return Ok(MapConfirmationDto(confirmation));
    }

    private static IActionResult? ValidateDeliveryAccess(DeliveryJob job, CallerScope? callerScope, List<string> roles)
    {
        var isCompanyAdmin = roles.Contains(SelloraRoles.SystemAdmin) || roles.Contains(SelloraRoles.CompanyAdmin);
        if (isCompanyAdmin)
        {
            return null;
        }

        if (roles.Contains(SelloraRoles.ShopOwner) && job.ShopId != callerScope?.ShopId)
            return new NotFoundResult();

        if (roles.Contains(SelloraRoles.SalesRep) && job.AssignedRepId != callerScope?.SalesRepId)
            return new NotFoundResult();

        if (roles.Contains(SelloraRoles.AgencyOperator) && job.AgencyId != callerScope?.AgencyId)
            return new NotFoundResult();

        if (roles.Contains(SelloraRoles.AreaManager) && (callerScope?.ProvinceIds == null || !callerScope.ProvinceIds.Contains(job.ProvinceId)))
            return new NotFoundResult();

        return null;
    }

    private static ConfirmationStateDto? ResolveConfirmationState(DeliveryJob job, DeliveryConfirmation? confirmation)
    {
        if (job.FulfilmentType == "ImmediateCashSale")
        {
            return null;
        }

        if (confirmation != null)
        {
            return MapConfirmationDto(confirmation);
        }

        if (job.Status == DeliveryStatus.Delivered)
        {
            return new ConfirmationStateDto("Awaiting", null, null, "Awaiting", null, null, null);
        }

        return null;
    }

    private static ConfirmationStateDto MapConfirmationDto(DeliveryConfirmation confirmation)
    {
        return new ConfirmationStateDto(
            confirmation.Type.ToString(),
            confirmation.Notes,
            confirmation.ConfirmedAt,
            confirmation.Type.ToString(),
            confirmation.Notes,
            confirmation.ConfirmedAt,
            confirmation.SignatureUrl
        );
    }

    private static IActionResult? ValidateJobEligibility(DeliveryJob job, CallerScope? callerScope)
    {
        if (callerScope?.ShopId != null && job.ShopId != callerScope.ShopId)
        {
            return new ForbidResult();
        }

        if (job.Status != DeliveryStatus.Delivered)
        {
            return new ConflictObjectResult(new { detail = $"Delivery must be in Delivered status to be confirmed. Current status is {job.Status}." });
        }

        if (job.FulfilmentType == "ImmediateCashSale")
        {
            return new ConflictObjectResult(new { detail = "ImmediateCashSale deliveries do not require confirmation." });
        }

        return null;
    }

    private static IActionResult? ValidateRequestPayload(
        DeliveryConfirmationRequest request,
        out ConfirmationType confirmationType,
        out string? notes)
    {
        confirmationType = default;
        notes = request.Note ?? request.Notes;

        var requestedType = request.Outcome ?? request.Type;
        if (string.IsNullOrWhiteSpace(requestedType) || !Enum.TryParse(requestedType, true, out confirmationType))
        {
            return new BadRequestObjectResult(new { detail = "Outcome must be Confirmed or Disputed." });
        }

        if (confirmationType == ConfirmationType.Disputed && string.IsNullOrWhiteSpace(notes))
        {
            return new BadRequestObjectResult(new { detail = "Notes are required when disputing a delivery." });
        }

        if (notes?.Length > 1000)
        {
            return new BadRequestObjectResult(new { detail = "Notes cannot exceed 1000 characters." });
        }

        if (request.SignatureUrl?.Length > 2000)
        {
            return new BadRequestObjectResult(new { detail = "SignatureUrl cannot exceed 2000 characters." });
        }

        return null;
    }

    private static IActionResult HandleExistingConfirmation(DeliveryConfirmation existing, ConfirmationType requestedType)
    {
        if (existing.Type == requestedType)
        {
            return new OkObjectResult(MapConfirmationDto(existing));
        }

        return new ConflictObjectResult(new { detail = $"Delivery is already {existing.Type} and cannot be changed." });
    }

    private static OutboxMessage CreateConfirmationOutboxMessage(
        DeliveryJob job,
        DeliveryConfirmation confirmation,
        string userId,
        string role,
        string? correlationId)
    {
        var isConfirmed = confirmation.Type == ConfirmationType.Confirmed;
        var eventType = isConfirmed ? "DeliveryConfirmed" : "DeliveryDisputed";

        object eventPayload = isConfirmed
            ? DeliveryConfirmedEvent.Create(
                companyId: job.CompanyId,
                deliveryId: job.DeliveryJobId,
                orderId: job.OrderId,
                orderReference: job.OrderReference,
                confirmedAt: confirmation.ConfirmedAt,
                note: confirmation.Notes,
                signatureUrl: confirmation.SignatureUrl,
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
                actorRole: role,
                correlationId: correlationId)
            : DeliveryDisputedEvent.Create(
                companyId: job.CompanyId,
                deliveryId: job.DeliveryJobId,
                orderId: job.OrderId,
                orderReference: job.OrderReference,
                disputedAt: confirmation.ConfirmedAt,
                note: confirmation.Notes,
                signatureUrl: confirmation.SignatureUrl,
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
                actorRole: role,
                correlationId: correlationId);

        return new OutboxMessage(
            job.CompanyId,
            "sellora.delivery.v1",
            job.OrderReference,
            eventType,
            System.Text.Json.JsonSerializer.Serialize(eventPayload)
        );
    }

    private List<string> GetUserRoles()
    {
        return User.Claims
            .Where(c => c.Type == "roles" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value.Contains('/') ? c.Value.Split('/')[1] : c.Value)
            .ToList();
    }
}

public class AssignDeliveryRequest
{
    [System.Text.Json.Serialization.JsonRequired]
    public Guid SalesRepId { get; set; }

    [System.Text.Json.Serialization.JsonRequired]
    public DateOnly ScheduledDate { get; set; }
}

public class UpdateDeliveryStatusRequest
{
    public string Status { get; set; } = default!;
    public string? Reason { get; set; }

    [System.Text.Json.Serialization.JsonRequired]
    public uint Version { get; set; }
}

[ExcludeFromCodeCoverage]
public record DeliveryListResponse(
    IReadOnlyList<DeliveryListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

[ExcludeFromCodeCoverage]
public record DeliveryListItemDto(
    Guid Id,
    string DeliveryReference,
    string OrderReference,
    string ShopName,
    Guid TerritoryId,
    string Status,
    DateOnly? ScheduledDate,
    string? AssignedRepName,
    string AgencyName,
    string? ConfirmationState = null);

public class DeliveryConfirmationRequest
{
    public string? Type { get; set; }
    public string? Outcome { get; set; }
    
    public string? Note { get; set; }
    public string? Notes { get; set; }
    public string? SignatureUrl { get; set; }
}

[ExcludeFromCodeCoverage]
public record ConfirmationStateDto(
    string State,
    string? Note,
    DateTimeOffset? Time,
    string? Status = null,
    string? Notes = null,
    DateTimeOffset? ConfirmedAt = null,
    string? SignatureUrl = null
);

[ExcludeFromCodeCoverage]
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

[ExcludeFromCodeCoverage]
public record DeliveryLineDto(
    Guid LineId,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int ReturnableQuantity
);

[ExcludeFromCodeCoverage]
public record DeliveryStatusHistoryDto(
    string status,
    string actorRole,
    DateTimeOffset occurredAt,
    string? reason
);
