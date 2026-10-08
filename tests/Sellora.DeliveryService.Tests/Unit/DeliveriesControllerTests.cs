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
}
