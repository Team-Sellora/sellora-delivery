using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sellora.DeliveryService.Api.Contracts;
using Sellora.DeliveryService.Api.Controllers;
using Sellora.DeliveryService.Api.Security;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class ReturnsControllerTests
{
    private readonly Mock<IReturnRequestRepository> _returnRepoMock = new();
    private readonly Mock<IDeliveryJobRepository> _jobRepoMock = new();
    private readonly Mock<IOrganizationClient> _orgClientMock = new();
    private readonly Mock<IInventoryClient> _inventoryClientMock = new();
    private readonly Mock<IReturnReferenceGenerator> _refGenMock = new();
    private readonly Mock<IOutboxWriter> _outboxWriterMock = new();
    private readonly ReturnsController _controller;

    public ReturnsControllerTests()
    {
        _controller = new ReturnsController(
            _returnRepoMock.Object,
            _jobRepoMock.Object,
            _orgClientMock.Object,
            _inventoryClientMock.Object,
            _refGenMock.Object,
            _outboxWriterMock.Object);

        var mockTx = new Mock<IExecutionTransaction>();
        _returnRepoMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);

        _refGenMock.Setup(r => r.Generate(It.IsAny<DateTimeOffset>()))
            .Returns("RT-261011-A1B2C3");
    }

    private void SetUser(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("roles", role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static DeliveryJob CreateDeliveredJob(
        Guid? shopId = null,
        Guid? assignedRepId = null,
        Guid? orderSalesRepId = null,
        Guid? agencyId = null,
        Guid? productId = null,
        int deliveredQuantity = 20)
    {
        var prodId = productId ?? Guid.NewGuid();
        var lines = new List<DeliveryJobLine>
        {
            new(Guid.Empty, Guid.NewGuid(), prodId, "Product A", deliveredQuantity, 100m, deliveredQuantity * 100m)
        };

        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(),
            "DL-261011-0001",
            Guid.NewGuid(),
            "ORD-0001",
            "ScheduledDelivery",
            shopId ?? Guid.NewGuid(),
            "Shop 1",
            "Owner 1",
            "owner@shop.com",
            agencyId ?? Guid.NewGuid(),
            "Agency 1",
            "agency@test.com",
            Guid.NewGuid(),
            Guid.NewGuid(),
            orderSalesRepId ?? Guid.NewGuid(),
            deliveredQuantity * 100m,
            "LKR",
            lines);

        if (assignedRepId.HasValue)
        {
            job.Assign(assignedRepId.Value, "Rep A", DateOnly.FromDateTime(DateTime.UtcNow), "op", "AgencyOperator");
        }
        else
        {
            job.Assign(Guid.NewGuid(), "Rep B", DateOnly.FromDateTime(DateTime.UtcNow), "op", "AgencyOperator");
        }

        job.ChangeStatus(DeliveryStatus.InTransit, "rep", "SalesRep");
        job.ChangeStatus(DeliveryStatus.Delivered, "rep", "SalesRep");

        return job;
    }

    [Fact]
    public async Task CreateReturn_JobNotFound_Returns404()
    {
        SetUser("user-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob?)null);

        var result = await _controller.CreateReturn(new CreateReturnRequest { DeliveryJobId = Guid.NewGuid() }, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CreateReturn_JobOutsideCallerScope_Returns404()
    {
        var shopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();
        var job = CreateDeliveredJob(shopId: shopId);

        SetUser("user-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, otherShopId, [], "Shop Owner"));

        var result = await _controller.CreateReturn(new CreateReturnRequest { DeliveryJobId = job.DeliveryJobId }, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CreateReturn_JobVisibleButCallerNotAllowedRequester_Returns403()
    {
        var agencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);

        SetUser("user-op", SelloraRoles.AgencyOperator);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Operator"));

        var result = await _controller.CreateReturn(new CreateReturnRequest { DeliveryJobId = job.DeliveryJobId }, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreateReturn_JobNotDelivered_Returns409()
    {
        var shopId = Guid.NewGuid();
        var lines = new List<DeliveryJobLine> { new(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "P1", 10, 10m, 100m) };
        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "DL-01", Guid.NewGuid(), "ORD-01", "ScheduledDelivery",
            shopId, "Shop", "Owner", "email", Guid.NewGuid(), "Agency", "email",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, "LKR", lines);

        SetUser("user-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, shopId, [], "Owner"));

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = [new() { ProductId = lines[0].ProductId, Quantity = 2, Reason = "Damaged" }]
        }, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateReturn_DuplicateProduct_Returns400ProblemDetails()
    {
        var shopId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var job = CreateDeliveredJob(shopId: shopId, productId: prodId);

        SetUser("user-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, shopId, [], "Owner"));

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines =
            [
                new() { ProductId = prodId, Quantity = 2, Reason = "Broken" },
                new() { ProductId = prodId, Quantity = 3, Reason = "Expired" }
            ]
        }, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain("Duplicate product");
        problem.Extensions.Should().ContainKey("lineErrors");
    }

    [Fact]
    public async Task CreateReturn_OverReturnRequested_Returns400ProblemDetailsWithRemaining()
    {
        var shopId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var job = CreateDeliveredJob(shopId: shopId, productId: prodId, deliveredQuantity: 20);

        SetUser("user-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, shopId, [], "Owner"));

        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(job.DeliveryJobId, job.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // 18 already approved or requested
        var returnedMap = new Dictionary<Guid, int> { [prodId] = 18 };
        _returnRepoMock.Setup(r => r.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedMap);

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = [new() { ProductId = prodId, Quantity = 5, Reason = "Damaged" }]
        }, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Be("only 2 units of Product A remain returnable");
        problem.Extensions.Should().ContainKey("lineErrors");
    }

    [Fact]
    public async Task CreateReturn_ValidRequestByShopOwner_Returns201AndEnqueuesOutboxEvent()
    {
        var shopId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var job = CreateDeliveredJob(shopId: shopId, productId: prodId, deliveredQuantity: 20);

        SetUser("owner-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, shopId, [], "Owner"));

        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(job.DeliveryJobId, job.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _returnRepoMock.Setup(r => r.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = [new() { ProductId = prodId, Quantity = 5, Reason = "Defective packaging" }]
        }, CancellationToken.None);

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<CreateReturnResponse>().Subject;
        response.Status.Should().Be("Requested");
        response.ReturnReference.Should().Be("RT-261011-A1B2C3");

        _outboxWriterMock.Verify(o => o.Write(It.Is<OutboxMessage>(m =>
            m.EventType == "ReturnRequested" &&
            m.Topic == "sellora.delivery.v1" &&
            m.MessageKey == job.OrderReference)), Times.Once);

        _returnRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecideReturn_AnotherAgency_Returns404()
    {
        var agencyId = Guid.NewGuid();
        var otherAgencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, otherAgencyId, null, [], "Op"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DecideReturn_NonRequestedReturn_Returns409()
    {
        var agencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);
        returnRequest.Approve(Guid.NewGuid(), "op", "AgencyOperator", DateTimeOffset.UtcNow);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DecideReturn_RejectWithoutReason_Returns400()
    {
        var agencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Rejected", Reason = "" },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DecideReturn_RejectWithReason_Returns204AndEnqueuesReturnRejected()
    {
        var agencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-01", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _jobRepoMock.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Rejected", Reason = "Inspection failed" },
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        returnRequest.Status.Should().Be(ReturnStatus.Rejected);
        returnRequest.DecisionReason.Should().Be("Inspection failed");

        _outboxWriterMock.Verify(o => o.Write(It.Is<OutboxMessage>(m =>
            m.EventType == "ReturnRejected" &&
            m.Topic == "sellora.delivery.v1")), Times.Once);
    }

    [Fact]
    public async Task DecideReturn_ApproveWhenNoInventoryOwnerFound_Returns409()
    {
        var agencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        // Inventory returns null
        _inventoryClientMock.Setup(i => i.GetAgencyInventoryOwnerAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryOwnerSummary?)null);

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value!.ToString().Should().Contain(agencyId.ToString());
    }

    [Fact]
    public async Task DecideReturn_ApproveSuccess_Returns204AndEnqueuesReturnAccepted()
    {
        var agencyId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId, productId: prodId, deliveredQuantity: 20);

        var line = new ReturnRequestLine(Guid.Empty, job.CompanyId, prodId, "Product A", 5, "Damaged");
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-261011-A1B2C3", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, [line]);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        var inventoryOwnerId = Guid.NewGuid();
        _inventoryClientMock.Setup(i => i.GetAgencyInventoryOwnerAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventoryOwnerSummary(inventoryOwnerId, "Agency", agencyId, "Agency Stock"));

        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(job.DeliveryJobId, job.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Exclude this request itself so returnedQuantities is empty
        _returnRepoMock.Setup(r => r.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        returnRequest.Status.Should().Be(ReturnStatus.Approved);
        returnRequest.InventoryOwnerId.Should().Be(inventoryOwnerId);

        _outboxWriterMock.Verify(o => o.Write(It.Is<OutboxMessage>(m =>
            m.EventType == "ReturnAccepted" &&
            m.Topic == "sellora.delivery.v1" &&
            m.MessageKey == job.OrderReference &&
            m.Payload.Contains("ReturnAccepted"))), Times.Once);
    }

    [Fact]
    public async Task DecideReturn_ConcurrencyConflict_Returns409()
    {
        var agencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-01", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _jobRepoMock.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        _returnRepoMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Rejected", Reason = "Duplicate" },
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetReturns_FilterOutOfScope_Returns403()
    {
        var myShopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();

        SetUser("owner-1", SelloraRoles.ShopOwner);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, myShopId, [], "Owner"));

        var result = await _controller.GetReturns(new ReturnListFilter { ShopId = otherShopId }, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetReturns_ValidQuery_ReturnsPagedResult()
    {
        SetUser("admin-1", SelloraRoles.CompanyAdmin);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, [], "Admin"));

        var items = new List<ReturnRequestListItem>
        {
            new(Guid.NewGuid(), "RT-01", Guid.NewGuid(), "ORD-01", "Shop 1", ReturnStatus.Requested, DateTimeOffset.UtcNow, "ShopOwner", 2)
        };
        _returnRepoMock.Setup(r => r.ListAsync(It.IsAny<ReturnRequestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, 1));

        var result = await _controller.GetReturns(new ReturnListFilter { Page = 1, PageSize = 20 }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ReturnListResponse>().Subject;
        response.TotalCount.Should().Be(1);
        response.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task DecideReturn_ReturnNotFound_Returns404()
    {
        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReturnRequest?)null);

        var result = await _controller.DecideReturn(
            Guid.NewGuid(),
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DecideReturn_InvalidDecision_Returns400()
    {
        var agencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Maybe" },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetReturnById_NotFound_WhenMissing()
    {
        SetUser("admin-1", SelloraRoles.CompanyAdmin);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReturnRequest?)null);

        var result = await _controller.GetReturnById(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetReturnById_NotFound_WhenOutOfScope()
    {
        var agencyId = Guid.NewGuid();
        var otherAgencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-01", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _jobRepoMock.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, otherAgencyId, null, [], "Op"));

        var result = await _controller.GetReturnById(returnRequest.ReturnRequestId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetReturnById_Ok_WhenInScope()
    {
        var agencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);
        var line = new ReturnRequestLine(Guid.NewGuid(), job.CompanyId, Guid.NewGuid(), "Sample Product", 2, "Damaged");
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-01", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, [line]);

        SetUser("admin-1", SelloraRoles.CompanyAdmin);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _jobRepoMock.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, [], "Admin"));

        var result = await _controller.GetReturnById(returnRequest.ReturnRequestId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = ok.Value.Should().BeOfType<ReturnDetailResponse>().Subject;
        detail.ReturnReference.Should().Be("RT-01");
        detail.Lines.Should().HaveCount(1);
        detail.Lines[0].ProductName.Should().Be("Sample Product");
        detail.Lines[0].Quantity.Should().Be(2);
        detail.Lines[0].Reason.Should().Be("Damaged");
    }

    [Fact]
    public async Task CreateReturn_EmptyLines_Returns400()
    {
        var job = CreateDeliveredJob();
        SetUser("owner-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, job.ShopId, [], "Owner"));

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = []
        }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateReturn_ProductNotInJob_Returns400()
    {
        var job = CreateDeliveredJob();
        SetUser("owner-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, job.ShopId, [], "Owner"));

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = [new CreateReturnRequestLine { ProductId = Guid.NewGuid(), Quantity = 1, Reason = "Damaged" }]
        }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateReturn_ReasonTooLong_Returns400()
    {
        var job = CreateDeliveredJob();
        var prodId = job.Lines.First().ProductId;
        SetUser("owner-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, job.ShopId, [], "Owner"));

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = [new CreateReturnRequestLine { ProductId = prodId, Quantity = 1, Reason = new string('x', 501) }]
        }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateReturn_LockedJobNull_Returns409()
    {
        var job = CreateDeliveredJob();
        var prodId = job.Lines.First().ProductId;
        SetUser("owner-1", SelloraRoles.ShopOwner);
        _jobRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, job.ShopId, [], "Owner"));
        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(job.DeliveryJobId, job.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob?)null);

        var result = await _controller.CreateReturn(new CreateReturnRequest
        {
            DeliveryJobId = job.DeliveryJobId,
            Lines = [new CreateReturnRequestLine { ProductId = prodId, Quantity = 1, Reason = "Damaged" }]
        }, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DecideReturn_RejectReasonTooLong_Returns400()
    {
        var agencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Rejected", Reason = new string('a', 501) },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DecideReturn_ApprovalLockedJobNull_Returns409()
    {
        var agencyId = Guid.NewGuid();
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(), "RT-01", Guid.NewGuid(), Guid.NewGuid(), "ORD-01",
            Guid.NewGuid(), agencyId, Guid.NewGuid(), Guid.NewGuid(),
            "u1", "ShopOwner", DateTimeOffset.UtcNow, []);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));
        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(returnRequest.DeliveryJobId, returnRequest.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob?)null);

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DecideReturn_ApprovalQuantityExceedsRemaining_Returns409()
    {
        var agencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);
        var prodId = job.Lines.First().ProductId;
        var line = new ReturnRequestLine(Guid.NewGuid(), job.CompanyId, prodId, "Item", 10, "Damaged"); // 10 units requested, but job only has 5
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-01", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, [line]);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));
        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(returnRequest.DeliveryJobId, returnRequest.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _returnRepoMock.Setup(r => r.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task DecideReturn_ApprovalConcurrencyException_Returns409()
    {
        var agencyId = Guid.NewGuid();
        var job = CreateDeliveredJob(agencyId: agencyId);
        var prodId = job.Lines.First().ProductId;
        var line = new ReturnRequestLine(Guid.NewGuid(), job.CompanyId, prodId, "Item", 1, "Damaged");
        var returnRequest = ReturnRequest.Create(
            job.CompanyId, "RT-01", job.DeliveryJobId, job.OrderId, job.OrderReference,
            job.ShopId, agencyId, job.TerritoryId, job.ProvinceId,
            "u1", "ShopOwner", DateTimeOffset.UtcNow, [line]);

        SetUser("op-1", SelloraRoles.AgencyOperator);
        _returnRepoMock.Setup(r => r.GetByIdWithLinesAsync(returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnRequest);
        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, [], "Op"));
        _returnRepoMock.Setup(r => r.LockDeliveryJobAsync(returnRequest.DeliveryJobId, returnRequest.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _returnRepoMock.Setup(r => r.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, returnRequest.ReturnRequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        _returnRepoMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        var result = await _controller.DecideReturn(
            returnRequest.ReturnRequestId,
            new DecideReturnRequest { Decision = "Approved" },
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }
}
