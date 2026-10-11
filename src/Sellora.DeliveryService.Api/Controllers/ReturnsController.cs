using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sellora.DeliveryService.Api.Contracts;
using Sellora.DeliveryService.Api.Security;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Events;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Api.Controllers;

[ApiController]
[Route("api/returns")]
[Authorize]
public class ReturnsController : ControllerBase
{
    private readonly IReturnRequestRepository _returnRepository;
    private readonly IDeliveryJobRepository _deliveryJobRepository;
    private readonly IOrganizationClient _organizationClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IReturnReferenceGenerator _returnReferenceGenerator;
    private readonly IOutboxWriter _outboxWriter;

    public ReturnsController(
        IReturnRequestRepository returnRepository,
        IDeliveryJobRepository deliveryJobRepository,
        IOrganizationClient organizationClient,
        IInventoryClient inventoryClient,
        IReturnReferenceGenerator returnReferenceGenerator,
        IOutboxWriter outboxWriter)
    {
        _returnRepository = returnRepository;
        _deliveryJobRepository = deliveryJobRepository;
        _organizationClient = organizationClient;
        _inventoryClient = inventoryClient;
        _returnReferenceGenerator = returnReferenceGenerator;
        _outboxWriter = outboxWriter;
    }

    [HttpPost]
    public async Task<IActionResult> CreateReturn([FromBody] CreateReturnRequest request, CancellationToken ct)
    {
        var job = await _deliveryJobRepository.GetByIdWithDetailsAsync(request.DeliveryJobId, ct);
        if (job == null)
        {
            return NotFound();
        }

        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        var roles = GetUserRoles();

        var accessError = ValidateJobScope(job, callerScope, roles);
        if (accessError != null)
        {
            return accessError;
        }

        if (!IsAllowedReturnRequester(job, callerScope, roles))
        {
            return Forbid();
        }

        if (job.Status != DeliveryStatus.Delivered)
        {
            return Conflict(new { detail = $"Return requests can only be raised for delivered orders. Current status is {job.Status}." });
        }

        var validationError = ValidateReturnLines(request.Lines, job);
        if (validationError != null)
        {
            return validationError;
        }

        return await ProcessReturnCreationAsync(request, job, ct);
    }

    [HttpPut("{id:guid}/decision")]
    [Authorize(Policy = RolePolicies.AgencyWrite)]
    public async Task<IActionResult> DecideReturn(Guid id, [FromBody] DecideReturnRequest request, CancellationToken ct)
    {
        var returnRequest = await _returnRepository.GetByIdWithLinesAsync(id, ct);
        if (returnRequest == null)
        {
            return NotFound();
        }

        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        if (returnRequest.AgencyId != callerScope?.AgencyId)
        {
            return NotFound();
        }

        if (returnRequest.Status != ReturnStatus.Requested)
        {
            return Conflict(new { detail = $"Return request is in {returnRequest.Status} status and cannot be decided." });
        }

        if (string.Equals(request.Decision, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return await ProcessApprovalAsync(returnRequest, ct);
        }

        if (string.Equals(request.Decision, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return await ProcessRejectionAsync(returnRequest, request.Reason, ct);
        }

        return BadRequest(new { detail = "Decision must be Approved or Rejected." });
    }

    [HttpGet]
    public async Task<IActionResult> GetReturns([FromQuery] ReturnListFilter filter, CancellationToken ct)
    {
        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        var roles = GetUserRoles();

        if (!IsListFilterAllowed(filter, callerScope, roles))
        {
            return Forbid();
        }

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = new ReturnRequestQuery
        {
            Statuses = filter.Status,
            ShopId = filter.ShopId,
            AgencyId = filter.AgencyId,
            Page = page,
            PageSize = pageSize,
            IsCompanyAdmin = IsCompanyAdmin(roles),
            ScopeAgencyId = roles.Contains(SelloraRoles.AgencyOperator) ? callerScope?.AgencyId : null,
            ScopeSalesRepId = roles.Contains(SelloraRoles.SalesRep) ? callerScope?.SalesRepId : null,
            ScopeShopId = roles.Contains(SelloraRoles.ShopOwner) ? callerScope?.ShopId : null,
            ScopeProvinceIds = roles.Contains(SelloraRoles.AreaManager) ? callerScope?.ProvinceIds : null
        };

        var (items, totalCount) = await _returnRepository.ListAsync(query, ct);

        var dtos = items.Select(i => new ReturnListItemDto(
            i.ReturnRequestId,
            i.ReturnReference,
            i.DeliveryJobId,
            i.OrderReference,
            i.ShopName,
            i.Status.ToString(),
            i.RequestedAt,
            i.RequestedByRole,
            i.LineCount)).ToList();

        return Ok(new ReturnListResponse(dtos, page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReturnById(Guid id, CancellationToken ct)
    {
        var returnRequest = await _returnRepository.GetByIdWithLinesAsync(id, ct);
        if (returnRequest == null)
        {
            return NotFound();
        }

        var job = await _deliveryJobRepository.GetByIdAsync(returnRequest.DeliveryJobId, ct);
        var callerScope = await _organizationClient.GetCallerScopeAsync(ct);
        var roles = GetUserRoles();

        if (!HasReturnAccess(returnRequest, job, callerScope, roles))
        {
            return NotFound();
        }

        var lineDtos = returnRequest.Lines.Select(l => new ReturnLineDto(
            l.ProductId,
            l.ProductName,
            l.Quantity,
            l.Reason)).ToList();

        var response = new ReturnDetailResponse(
            returnRequest.ReturnRequestId,
            returnRequest.ReturnReference,
            returnRequest.DeliveryJobId,
            returnRequest.OrderId,
            returnRequest.OrderReference,
            returnRequest.ShopId,
            job?.ShopName ?? string.Empty,
            returnRequest.AgencyId,
            job?.AgencyName ?? string.Empty,
            returnRequest.TerritoryId,
            returnRequest.ProvinceId,
            returnRequest.Status.ToString(),
            returnRequest.RequestedByUserId,
            returnRequest.RequestedByRole,
            returnRequest.RequestedAt,
            returnRequest.DecidedByUserId,
            returnRequest.DecidedByRole,
            returnRequest.DecidedAt,
            returnRequest.DecisionReason,
            returnRequest.InventoryOwnerId,
            lineDtos);

        return Ok(response);
    }

    private async Task<IActionResult> ProcessReturnCreationAsync(
        CreateReturnRequest request,
        DeliveryJob job,
        CancellationToken ct)
    {
        await using var tx = await _returnRepository.BeginTransactionAsync(ct);

        var lockedJob = await _returnRepository.LockDeliveryJobAsync(job.DeliveryJobId, job.CompanyId, ct);
        if (lockedJob == null || lockedJob.Status != DeliveryStatus.Delivered)
        {
            return Conflict(new { detail = "Delivery job status has changed or is not available." });
        }

        // Pending requests count toward the remainder to prevent concurrent pending requests from exceeding stock
        var returnedQuantities = await _returnRepository.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, null, ct);

        foreach (var line in request.Lines)
        {
            var jobLine = lockedJob.Lines.First(l => l.ProductId == line.ProductId);
            var alreadyReturned = returnedQuantities.GetValueOrDefault(line.ProductId, 0);
            var remaining = jobLine.Quantity - alreadyReturned;

            if (line.Quantity > remaining)
            {
                var message = $"only {remaining} units of {jobLine.ProductName} remain returnable";
                return CreateLineProblem(line.ProductId, message);
            }
        }

        var returnRef = _returnReferenceGenerator.Generate(DateTimeOffset.UtcNow);
        var domainLines = request.Lines.Select(l =>
        {
            var jobLine = lockedJob.Lines.First(jl => jl.ProductId == l.ProductId);
            return new ReturnRequestLine(Guid.Empty, job.CompanyId, l.ProductId, jobLine.ProductName, l.Quantity, l.Reason.Trim());
        });

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var primaryRole = GetUserRoles().FirstOrDefault() ?? "ShopOwner";

        var returnRequest = ReturnRequest.Create(
            job.CompanyId,
            returnRef,
            job.DeliveryJobId,
            job.OrderId,
            job.OrderReference,
            job.ShopId,
            job.AgencyId,
            job.TerritoryId,
            job.ProvinceId,
            userId,
            primaryRole,
            DateTimeOffset.UtcNow,
            domainLines);

        await _returnRepository.AddAsync(returnRequest, ct);

        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        var returnRequestedEvent = ReturnRequestedEvent.Create(
            job.CompanyId,
            returnRequest.ReturnRequestId,
            returnRequest.ReturnReference,
            job.DeliveryJobId,
            job.OrderId,
            job.OrderReference,
            job.ShopId,
            job.ShopName,
            job.ShopOwnerName,
            job.ShopOwnerEmail,
            job.AgencyId,
            job.AgencyName,
            job.AgencyEmail,
            job.TerritoryId,
            job.ProvinceId,
            returnRequest.RequestedAt,
            userId,
            primaryRole,
            returnRequest.Lines.Select(l => new ReturnRequestedLine(l.ProductId, l.ProductName, l.Quantity, l.Reason)),
            correlationId);

        var outboxMessage = new OutboxMessage(
            job.CompanyId,
            "sellora.delivery.v1",
            job.OrderReference,
            "ReturnRequested",
            JsonSerializer.Serialize(returnRequestedEvent));

        _outboxWriter.Write(outboxMessage);

        await _returnRepository.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new CreateReturnResponse(
            returnRequest.ReturnRequestId,
            returnRequest.ReturnReference,
            returnRequest.Status.ToString()));
    }

    private async Task<IActionResult> ProcessApprovalAsync(ReturnRequest returnRequest, CancellationToken ct)
    {
        // Resolve stock owner from Inventory before opening DB transaction to avoid holding locks during HTTP calls
        var agencyOwner = await _inventoryClient.GetAgencyInventoryOwnerAsync(returnRequest.AgencyId, ct);
        if (agencyOwner == null)
        {
            return Conflict(new { detail = $"No inventory owner found for agency {returnRequest.AgencyId}." });
        }

        await using var tx = await _returnRepository.BeginTransactionAsync(ct);

        var lockedJob = await _returnRepository.LockDeliveryJobAsync(returnRequest.DeliveryJobId, returnRequest.CompanyId, ct);
        if (lockedJob == null)
        {
            return Conflict(new { detail = "Delivery job not found." });
        }

        // Exclude this request itself from the check to avoid counting its own quantity twice
        var returnedQuantities = await _returnRepository.GetReturnedQuantitiesByJobIdAsync(
            returnRequest.DeliveryJobId, excludeReturnRequestId: returnRequest.ReturnRequestId, ct);

        foreach (var line in returnRequest.Lines)
        {
            var jobLine = lockedJob.Lines.FirstOrDefault(jl => jl.ProductId == line.ProductId);
            var remaining = (jobLine?.Quantity ?? 0) - returnedQuantities.GetValueOrDefault(line.ProductId, 0);

            if (line.Quantity > remaining)
            {
                return Conflict(new { detail = $"Only {remaining} units of {line.ProductName} remain returnable." });
            }
        }

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var primaryRole = GetUserRoles().FirstOrDefault() ?? "AgencyOperator";

        returnRequest.Approve(agencyOwner.InventoryOwnerId, userId, primaryRole, DateTimeOffset.UtcNow);

        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

        // batchId is null because Delivery does not track original batch; Inventory accepts null batches for returns
        var acceptedLines = returnRequest.Lines.Select(l => new ReturnAcceptedLine(
            l.ProductId,
            null,
            l.Quantity,
            l.ProductName));

        var returnAcceptedEvent = ReturnAcceptedEvent.Create(
            returnRequest.CompanyId,
            returnRequest.ReturnRequestId,
            agencyOwner.InventoryOwnerId,
            returnRequest.ReturnReference,
            returnRequest.DecidedAt!.Value,
            correlationId,
            lockedJob.DeliveryJobId,
            lockedJob.OrderReference,
            lockedJob.ShopId,
            lockedJob.ShopName,
            lockedJob.ShopOwnerName,
            lockedJob.ShopOwnerEmail,
            lockedJob.AgencyId,
            lockedJob.AgencyName,
            lockedJob.AgencyEmail,
            acceptedLines);

        var outboxMessage = new OutboxMessage(
            returnRequest.CompanyId,
            "sellora.delivery.v1",
            lockedJob.OrderReference,
            "ReturnAccepted",
            JsonSerializer.Serialize(returnAcceptedEvent));

        _outboxWriter.Write(outboxMessage);

        try
        {
            await _returnRepository.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { detail = "Concurrency conflict. The return request has been updated by another process." });
        }

        return NoContent();
    }

    private async Task<IActionResult> ProcessRejectionAsync(ReturnRequest returnRequest, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest(new { detail = "A reason is required when rejecting a return request." });
        }

        if (reason.Length > 500)
        {
            return BadRequest(new { detail = "Reason cannot exceed 500 characters." });
        }

        var userId = User.FindFirst("sub")?.Value ?? "unknown";
        var primaryRole = GetUserRoles().FirstOrDefault() ?? "AgencyOperator";

        returnRequest.Reject(reason, userId, primaryRole, DateTimeOffset.UtcNow);

        var job = await _deliveryJobRepository.GetByIdAsync(returnRequest.DeliveryJobId, ct);
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var returnRejectedEvent = ReturnRejectedEvent.Create(
            returnRequest.CompanyId,
            returnRequest.ReturnRequestId,
            returnRequest.ReturnReference,
            returnRequest.DeliveryJobId,
            returnRequest.OrderId,
            returnRequest.OrderReference,
            returnRequest.ShopId,
            job?.ShopName ?? string.Empty,
            job?.ShopOwnerName ?? string.Empty,
            job?.ShopOwnerEmail ?? string.Empty,
            returnRequest.AgencyId,
            job?.AgencyName ?? string.Empty,
            job?.AgencyEmail ?? string.Empty,
            returnRequest.DecidedAt!.Value,
            userId,
            primaryRole,
            returnRequest.DecisionReason!,
            correlationId);

        var outboxMessage = new OutboxMessage(
            returnRequest.CompanyId,
            "sellora.delivery.v1",
            returnRequest.OrderReference,
            "ReturnRejected",
            JsonSerializer.Serialize(returnRejectedEvent));

        _outboxWriter.Write(outboxMessage);

        try
        {
            await _returnRepository.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { detail = "Concurrency conflict. The return request has been updated by another process." });
        }

        return NoContent();
    }

    private static IActionResult? ValidateReturnLines(List<CreateReturnRequestLine>? lines, DeliveryJob job)
    {
        if (lines == null || lines.Count == 0)
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "At least one line is required."
            });
        }

        var duplicate = lines.GroupBy(l => l.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            return CreateLineProblem(duplicate.Key, $"Duplicate product {duplicate.Key} in return request.");
        }

        foreach (var line in lines)
        {
            var jobLine = job.Lines.FirstOrDefault(jl => jl.ProductId == line.ProductId);
            if (jobLine == null)
            {
                return CreateLineProblem(line.ProductId, $"Product {line.ProductId} is not part of delivery job.");
            }

            if (line.Quantity <= 0)
            {
                return CreateLineProblem(line.ProductId, $"Quantity for product {jobLine.ProductName} must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(line.Reason) || line.Reason.Length > 500)
            {
                return CreateLineProblem(line.ProductId, $"Reason for product {jobLine.ProductName} is required and cannot exceed 500 characters.");
            }
        }

        return null;
    }

    private static IActionResult CreateLineProblem(Guid productId, string message)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = message
        };
        problem.Extensions["lineErrors"] = new[]
        {
            new { productId, message }
        };

        return new BadRequestObjectResult(problem);
    }

    private static bool IsCompanyAdmin(List<string> roles) =>
        roles.Contains(SelloraRoles.SystemAdmin) || roles.Contains(SelloraRoles.CompanyAdmin);

    private static IActionResult? ValidateJobScope(DeliveryJob job, CallerScope? callerScope, List<string> roles)
    {
        if (IsCompanyAdmin(roles)) return null;

        if (roles.Contains(SelloraRoles.ShopOwner) && job.ShopId != callerScope?.ShopId)
            return new NotFoundResult();

        if (roles.Contains(SelloraRoles.SalesRep) &&
            job.AssignedRepId != callerScope?.SalesRepId &&
            job.OrderSalesRepId != callerScope?.SalesRepId)
            return new NotFoundResult();

        if (roles.Contains(SelloraRoles.AgencyOperator) && job.AgencyId != callerScope?.AgencyId)
            return new NotFoundResult();

        if (roles.Contains(SelloraRoles.AreaManager) &&
            (callerScope?.ProvinceIds == null || !callerScope.ProvinceIds.Contains(job.ProvinceId)))
            return new NotFoundResult();

        return null;
    }

    private static bool IsAllowedReturnRequester(DeliveryJob job, CallerScope? callerScope, List<string> roles)
    {
        if (roles.Contains(SelloraRoles.ShopOwner) && job.ShopId == callerScope?.ShopId)
            return true;

        if (roles.Contains(SelloraRoles.SalesRep) &&
            (job.AssignedRepId == callerScope?.SalesRepId || job.OrderSalesRepId == callerScope?.SalesRepId))
            return true;

        return false;
    }

    private static bool IsListFilterAllowed(ReturnListFilter filter, CallerScope? callerScope, List<string> roles)
    {
        if (IsCompanyAdmin(roles)) return true;

        if (roles.Contains(SelloraRoles.ShopOwner) && filter.ShopId.HasValue && filter.ShopId.Value != callerScope?.ShopId)
            return false;

        if (roles.Contains(SelloraRoles.AgencyOperator) && filter.AgencyId.HasValue && filter.AgencyId.Value != callerScope?.AgencyId)
            return false;

        return true;
    }

    private static bool HasReturnAccess(ReturnRequest returnRequest, DeliveryJob? job, CallerScope? callerScope, List<string> roles)
    {
        if (IsCompanyAdmin(roles)) return true;

        if (roles.Contains(SelloraRoles.ShopOwner) && returnRequest.ShopId == callerScope?.ShopId)
            return true;

        if (roles.Contains(SelloraRoles.AgencyOperator) && returnRequest.AgencyId == callerScope?.AgencyId)
            return true;

        if (roles.Contains(SelloraRoles.AreaManager) &&
            callerScope?.ProvinceIds != null && callerScope.ProvinceIds.Contains(returnRequest.ProvinceId))
            return true;

        if (roles.Contains(SelloraRoles.SalesRep) && job != null &&
            (job.AssignedRepId == callerScope?.SalesRepId || job.OrderSalesRepId == callerScope?.SalesRepId))
            return true;

        return false;
    }

    private List<string> GetUserRoles()
    {
        return User.Claims
            .Where(c => c.Type == "roles" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value.Contains('/') ? c.Value.Split('/')[1] : c.Value)
            .ToList();
    }
}
