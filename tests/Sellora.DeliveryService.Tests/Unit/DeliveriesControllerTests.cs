using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Sellora.DeliveryService.Api.Controllers;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sellora.DeliveryService.Application.Events;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveriesControllerTests
{
    private readonly Mock<IDeliveryJobRepository> _mockRepository;
    private readonly Mock<IOrganizationClient> _mockOrganizationClient;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly Mock<IOutboxWriter> _mockOutboxWriter;
    private readonly DeliveriesController _controller;
    private readonly Guid _agencyId = Guid.NewGuid();

    public DeliveriesControllerTests()
    {
        _mockRepository = new Mock<IDeliveryJobRepository>();
        _mockOrganizationClient = new Mock<IOrganizationClient>();
        _mockTenantContext = new Mock<ITenantContext>();
        _mockOutboxWriter = new Mock<IOutboxWriter>();
        
        _controller = new DeliveriesController(
            _mockRepository.Object, 
            _mockOrganizationClient.Object,
            _mockTenantContext.Object,
            _mockOutboxWriter.Object);
            
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "test-user-id"),
            new Claim("roles", "AgencyOperator")
        ]));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, _agencyId, null, Array.Empty<Guid>(), "test-user"));
    }

    private DeliveryJob CreateTestJob(DeliveryStatus status = DeliveryStatus.Pending, Guid? territoryId = null)
    {
        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "REF-123", Guid.NewGuid(), "ORD-1", "Delivery",
            Guid.NewGuid(), "Shop", "Owner", "email@test.com",
            _agencyId, "Agency", "agency@test.com",
            territoryId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1000m, "LKR", new List<DeliveryJobLine>());
            
        if (status == DeliveryStatus.Assigned)
        {
            job.Assign(Guid.NewGuid(), "Test Rep", DateOnly.FromDateTime(DateTime.UtcNow), "test-user-id", "AgencyOperator");
        }
        
        return job;
    }

    [Fact]
    public async Task GetEligibleReps_WhenJobNotFound_ReturnsNotFound()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob)null!);

        // Act
        var result = await _controller.GetEligibleReps(jobId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetEligibleReps_WhenJobFound_ReturnsRepsForTerritory()
    {
        // Arrange
        var territoryId = Guid.NewGuid();
        var job = CreateTestJob(territoryId: territoryId);
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
            
        var allReps = new List<SalesRepSummary>
        {
            new(Guid.NewGuid(), "Rep 1", null, "Active", new TerritoryReference(territoryId, "T1", "Territory 1")),
            new(Guid.NewGuid(), "Rep 2", null, "Active", new TerritoryReference(Guid.NewGuid(), "T2", "Territory 2")),
            new(Guid.NewGuid(), "Rep 3", null, "Inactive", new TerritoryReference(territoryId, "T1", "Territory 1"))
        };
        
        _mockOrganizationClient.Setup(c => c.ListSalesRepsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(allReps);

        // Act
        var result = await _controller.GetEligibleReps(job.DeliveryJobId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedReps = okResult.Value.Should().BeAssignableTo<IEnumerable<SalesRepSummary>>().Subject.ToList();
        
        returnedReps.Should().ContainSingle();
        returnedReps[0].DisplayName.Should().Be("Rep 1");
    }

    [Fact]
    public async Task AssignDeliveryJob_WhenJobNotFound_ReturnsNotFound()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob)null!);

        // Act
        var result = await _controller.AssignDeliveryJob(jobId, new AssignDeliveryRequest(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task AssignDeliveryJob_WhenJobNotPending_ReturnsConflict()
    {
        // Arrange
        var job = CreateTestJob(status: DeliveryStatus.Assigned);
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var result = await _controller.AssignDeliveryJob(job.DeliveryJobId, new AssignDeliveryRequest(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task AssignDeliveryJob_WhenDateInPast_ReturnsBadRequest()
    {
        // Arrange
        var job = CreateTestJob();
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new AssignDeliveryRequest 
        { 
            SalesRepId = Guid.NewGuid(), 
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)) 
        };

        // Act
        var result = await _controller.AssignDeliveryJob(job.DeliveryJobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AssignDeliveryJob_WhenRepNotEligible_ReturnsBadRequest()
    {
        // Arrange
        var territoryId = Guid.NewGuid();
        var job = CreateTestJob(territoryId: territoryId);
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mockOrganizationClient.Setup(c => c.ListSalesRepsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SalesRepSummary>()); // No eligible reps

        var request = new AssignDeliveryRequest 
        { 
            SalesRepId = Guid.NewGuid(), 
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) 
        };

        // Act
        var result = await _controller.AssignDeliveryJob(job.DeliveryJobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task AssignDeliveryJob_WhenValid_AssignsAndReturnsNoContent()
    {
        // Arrange
        var territoryId = Guid.NewGuid();
        var repId = Guid.NewGuid();
        
        var job = CreateTestJob(territoryId: territoryId);
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mockOrganizationClient.Setup(c => c.ListSalesRepsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SalesRepSummary> 
            { 
                new(repId, "Valid Rep", null, "Active", new TerritoryReference(territoryId, "T1", "Territory 1"))
            });

        var request = new AssignDeliveryRequest 
        { 
            SalesRepId = repId, 
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) 
        };

        // Act
        var result = await _controller.AssignDeliveryJob(job.DeliveryJobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        job.Status.Should().Be(DeliveryStatus.Assigned);
        job.AssignedRepId.Should().Be(repId);
        job.AssignedRepName.Should().Be("Valid Rep");
        
        _mockOutboxWriter.Verify(w => w.Write(It.IsAny<OutboxMessage>()), Times.Once);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task UpdateStatus_WhenInvalidStatus_ReturnsBadRequest()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest { Status = "InvalidStatus" };

        // Act
        var result = await _controller.UpdateStatus(jobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateStatus_WhenFailedWithoutReason_ReturnsBadRequest()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest { Status = "Failed", Reason = "" };

        // Act
        var result = await _controller.UpdateStatus(jobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateStatus_WhenReasonTooLong_ReturnsBadRequest()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest { Status = "Failed", Reason = new string('A', 501) };

        // Act
        var result = await _controller.UpdateStatus(jobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateStatus_WhenJobNotFound_ReturnsNotFound()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest { Status = "Delivered" };
        
        _mockRepository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob)null!);
            
        var repId = Guid.NewGuid();
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(repId, _agencyId, null, Array.Empty<Guid>(), "test-user"));

        // Act
        var result = await _controller.UpdateStatus(jobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateStatus_WhenNotAssignedRep_ReturnsForbid()
    {
        // Arrange
        var job = CreateTestJob(status: DeliveryStatus.Assigned);
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
            
        var differentRepId = Guid.NewGuid();
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(differentRepId, _agencyId, null, Array.Empty<Guid>(), "test-user"));

        var request = new UpdateDeliveryStatusRequest { Status = "Delivered" };

        // Act
        var result = await _controller.UpdateStatus(job.DeliveryJobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task UpdateStatus_WhenInvalidTransition_ReturnsConflict()
    {
        // Arrange
        var job = CreateTestJob(status: DeliveryStatus.Assigned); // Assigned -> Cancelled is allowed, but PATCH only allows InTransit, Delivered, Failed. Assigned -> Delivered is NOT allowed.
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
            
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(job.AssignedRepId, _agencyId, null, Array.Empty<Guid>(), "test-user"));

        var request = new UpdateDeliveryStatusRequest { Status = "Delivered" }; // Assigned -> Delivered is invalid

        // Act
        var result = await _controller.UpdateStatus(job.DeliveryJobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task UpdateStatus_WhenValid_UpdatesStatusAndReturnsNoContent()
    {
        // Arrange
        var job = CreateTestJob(status: DeliveryStatus.Assigned);
        // Force job into InTransit
        job.ChangeStatus(DeliveryStatus.InTransit, "system", "system");
        
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
            
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(job.AssignedRepId, _agencyId, null, Array.Empty<Guid>(), "test-user"));

        var request = new UpdateDeliveryStatusRequest { Status = "Delivered", Version = 123 };

        // Act
        var result = await _controller.UpdateStatus(job.DeliveryJobId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        job.Status.Should().Be(DeliveryStatus.Delivered);
        job.DeliveredAt.Should().NotBeNull();
        
        _mockRepository.Verify(r => r.SetOriginalVersion(job, 123), Times.Once);
        _mockOutboxWriter.Verify(w => w.Write(It.IsAny<OutboxMessage>()), Times.Once);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task GetDeliveries_ReturnsOkResult_WhenValid()
    {
        // Arrange
        _mockRepository.Setup(r => r.ListAsync(It.IsAny<DeliveryJobQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DeliveryJob> { CreateTestJob() }, 1));
            
        // Act
        var result = await _controller.GetDeliveries(null, null, null, null, null, null, false, 1, 10, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetDeliveryById_ReturnsOkResult_WhenValid()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestJob());
            
        // Act
        var result = await _controller.GetDeliveryById(jobId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData("ShopOwner")]
    [InlineData("SalesRep")]
    [InlineData("AreaManager")]
    [InlineData("AgencyOperator")]
    public async Task GetDeliveryById_ReturnsNotFound_WhenWrongScope(string role)
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var job = CreateTestJob(DeliveryStatus.Assigned); 
        job.Assign(Guid.NewGuid(), "Other Rep", DateOnly.MaxValue, "sys", "sys"); 
        
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // specifically setting up caller scope to mismatch
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>(), "test"));

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "test-user-id"),
            new Claim("roles", $"PRIMARY/{role}")
        ], "TestAuth"));
        
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
            
        // Act
        var result = await _controller.GetDeliveryById(jobId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetDeliveries_ReturnsForbid_WhenWrongShopScope()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "test-user-id"),
            new Claim("roles", "PRIMARY/ShopOwner")
        ], "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>(), "test"));

        var otherShopId = Guid.NewGuid();

        // Act
        var result = await _controller.GetDeliveries(null, null, null, null, otherShopId, null, false, 1, 10, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetDeliveries_ReturnsForbid_WhenWrongSalesRepScope()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "test-user-id"),
            new Claim("roles", "PRIMARY/SalesRep")
        ], "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>(), "test"));

        var otherSalesRepId = Guid.NewGuid();

        // Act
        var result = await _controller.GetDeliveries(null, null, null, otherSalesRepId, null, null, false, 1, 10, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetDeliveries_ReturnsOk_WhenCompanyAdmin()
    {
        // Arrange — CompanyAdmin bypasses all scope checks and goes straight to ListAsync
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "admin-user-id"),
            new Claim("roles", "PRIMARY/CompanyAdmin")
        ], "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, Array.Empty<Guid>(), "admin"));

        _mockRepository.Setup(r => r.ListAsync(It.IsAny<DeliveryJobQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DeliveryJob> { CreateTestJob() }, 1));

        // Act
        var result = await _controller.GetDeliveries(null, null, null, null, null, null, false, 1, 10, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _mockRepository.Verify(r => r.ListAsync(
            It.Is<DeliveryJobQuery>(q => q.IsCompanyAdmin == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDeliveryById_ReturnsOk_WhenCompanyAdmin()
    {
        // Arrange — CompanyAdmin bypasses scope checks and receives the full DTO
        var jobId = Guid.NewGuid();
        var job = CreateTestJob();

        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "admin-user-id"),
            new Claim("roles", "PRIMARY/CompanyAdmin")
        ], "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, Array.Empty<Guid>(), "admin"));

        // Act
        var result = await _controller.GetDeliveryById(jobId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetDeliveries_ReturnsOk_WhenRoleHasNoSlashPrefix()
    {
        // Arrange — tests the GetUserRoles() branch where claim value has NO '/' prefix
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "test-user-id"),
            new Claim("roles", "AgencyOperator")  // no "PRIMARY/" prefix
        ], "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, Guid.NewGuid(), null, Array.Empty<Guid>(), "test"));

        _mockRepository.Setup(r => r.ListAsync(It.IsAny<DeliveryJobQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DeliveryJob>(), 0));

        // Act
        var result = await _controller.GetDeliveries(null, null, null, null, null, null, false, 1, 10, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetDeliveryById_ReturnsNotFound_WhenDeliveryNotFound()
    {
        var deliveryId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob?)null);

        var result = await _controller.GetDeliveryById(deliveryId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void DeliveryDtoRecords_PropertiesCanBeRead()
    {
        var lineDto = new DeliveryLineDto(Guid.NewGuid(), Guid.NewGuid(), "Product A", 2, 10.5m, 21.0m, 2);
        lineDto.LineId.Should().NotBeEmpty();
        lineDto.ProductId.Should().NotBeEmpty();
        lineDto.ProductName.Should().Be("Product A");
        lineDto.Quantity.Should().Be(2);
        lineDto.UnitPrice.Should().Be(10.5m);
        lineDto.LineTotal.Should().Be(21.0m);
        lineDto.ReturnableQuantity.Should().Be(2);

        var historyDto = new DeliveryStatusHistoryDto("Pending", "ShopOwner", DateTimeOffset.UtcNow, "initial");
        historyDto.status.Should().Be("Pending");
        historyDto.actorRole.Should().Be("ShopOwner");
        historyDto.occurredAt.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
        historyDto.reason.Should().Be("initial");

        var itemDto = new DeliveryListItemDto(
            Guid.NewGuid(), "DEL-001", "ORD-001", "Shop 1", Guid.NewGuid(), "Pending",
            new DateOnly(2026, 10, 10), "Rep Name", "Agency Name");
        itemDto.Id.Should().NotBeEmpty();
        itemDto.DeliveryReference.Should().Be("DEL-001");
        itemDto.OrderReference.Should().Be("ORD-001");
        itemDto.ShopName.Should().Be("Shop 1");
        itemDto.TerritoryId.Should().NotBeEmpty();
        itemDto.Status.Should().Be("Pending");
        itemDto.ScheduledDate.Should().Be(new DateOnly(2026, 10, 10));
        itemDto.AssignedRepName.Should().Be("Rep Name");
        itemDto.AgencyName.Should().Be("Agency Name");

        var listResponse = new DeliveryListResponse([itemDto], 1, 20, 1);
        listResponse.Items.Should().HaveCount(1);
        listResponse.Page.Should().Be(1);
        listResponse.PageSize.Should().Be(20);
        listResponse.TotalCount.Should().Be(1);

        var detailResponse = new DeliveryDetailResponse(
            Guid.NewGuid(), "DEL-001", Guid.NewGuid(), "ORD-001", "ScheduledDelivery",
            Guid.NewGuid(), "Shop 1", "Owner", "owner@test.com",
            Guid.NewGuid(), "Agency 1", "agency@test.com",
            Guid.NewGuid(), Guid.NewGuid(), 100m, "LKR", "Pending",
            Guid.NewGuid(), "Rep Name", new DateOnly(2026, 10, 10), null,
            DateTimeOffset.UtcNow, 1, [lineDto], [historyDto], null);

        detailResponse.Id.Should().NotBeEmpty();
        detailResponse.DeliveryReference.Should().Be("DEL-001");
        detailResponse.OrderId.Should().NotBeEmpty();
        detailResponse.OrderReference.Should().Be("ORD-001");
        detailResponse.FulfilmentType.Should().Be("ScheduledDelivery");
        detailResponse.ShopId.Should().NotBeEmpty();
        detailResponse.ShopName.Should().Be("Shop 1");
        detailResponse.ShopOwnerName.Should().Be("Owner");
        detailResponse.ShopOwnerEmail.Should().Be("owner@test.com");
        detailResponse.AgencyId.Should().NotBeEmpty();
        detailResponse.AgencyName.Should().Be("Agency 1");
        detailResponse.AgencyEmail.Should().Be("agency@test.com");
        detailResponse.TerritoryId.Should().NotBeEmpty();
        detailResponse.ProvinceId.Should().NotBeEmpty();
        detailResponse.Total.Should().Be(100m);
        detailResponse.Currency.Should().Be("LKR");
        detailResponse.Status.Should().Be("Pending");
        detailResponse.AssignedRepId.Should().NotBeEmpty();
        detailResponse.AssignedRepName.Should().Be("Rep Name");
        detailResponse.ScheduledDate.Should().Be(new DateOnly(2026, 10, 10));
        detailResponse.DeliveredAt.Should().BeNull();
        detailResponse.CreatedAt.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
        detailResponse.Version.Should().Be(1);
        detailResponse.Lines.Should().HaveCount(1);
        detailResponse.History.Should().HaveCount(1);
        detailResponse.Confirmation.Should().BeNull();
    }
}

