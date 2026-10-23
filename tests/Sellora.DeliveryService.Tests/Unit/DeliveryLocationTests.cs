using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Sellora.DeliveryService.Api.Controllers;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryLocationTests
{
    private readonly Mock<IDeliveryJobRepository> _mockRepository;
    private readonly Mock<IOrganizationClient> _mockOrganizationClient;
    private readonly Mock<IOutboxWriter> _mockOutboxWriter;
    private readonly FakeTimeProvider _timeProvider;
    private readonly DeliveriesController _controller;

    private readonly Guid _repId = Guid.NewGuid();
    private readonly Guid _otherRepId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedNow = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    public DeliveryLocationTests()
    {
        _mockRepository = new Mock<IDeliveryJobRepository>();
        _mockOrganizationClient = new Mock<IOrganizationClient>();
        _mockOutboxWriter = new Mock<IOutboxWriter>();
        _timeProvider = new FakeTimeProvider(_fixedNow);

        _controller = new DeliveriesController(
            _mockRepository.Object,
            _mockOrganizationClient.Object,
            _mockOutboxWriter.Object,
            returnRepository: null,
            logger: null,
            timeProvider: _timeProvider);

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "rep-user-id"),
            new Claim("roles", "SalesRep")
        ]));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(_repId, null, null, Array.Empty<Guid>(), "Test Rep"));
    }

    private DeliveryJob CreateInTransitJob(Guid? assignedRepId = null)
    {
        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "DEL-1", Guid.NewGuid(), "ORD-1", "ScheduledDelivery",
            Guid.NewGuid(), "Shop A", "Owner A", "owner@shop.com",
            Guid.NewGuid(), "Agency A", "agency@agency.com",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, "LKR",
            new List<DeliveryJobLine>());

        job.Assign(
            assignedRepId ?? _repId,
            "Test Rep",
            DateOnly.FromDateTime(DateTime.UtcNow),
            "op-user",
            "AgencyOperator");

        job.ChangeStatus(DeliveryStatus.InTransit, "rep-user", "SalesRep");
        return job;
    }

    [Fact]
    public async Task RecordLocation_WhenJobNotFound_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob)null!);

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(jobId, request, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RecordLocation_WhenCallerNotAssignedRep_ReturnsForbid()
    {
        var job = CreateInTransitJob(assignedRepId: _otherRepId);
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData(DeliveryStatus.Pending)]
    [InlineData(DeliveryStatus.Assigned)]
    [InlineData(DeliveryStatus.Delivered)]
    [InlineData(DeliveryStatus.Failed)]
    [InlineData(DeliveryStatus.Cancelled)]
    public async Task RecordLocation_WhenJobNotInTransit_ReturnsConflict(DeliveryStatus status)
    {
        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "DEL-1", Guid.NewGuid(), "ORD-1", "ScheduledDelivery",
            Guid.NewGuid(), "Shop A", "Owner A", "owner@shop.com",
            Guid.NewGuid(), "Agency A", "agency@agency.com",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, "LKR",
            new List<DeliveryJobLine>());

        if (status == DeliveryStatus.Pending)
        {
            typeof(DeliveryJob).GetProperty(nameof(DeliveryJob.AssignedRepId))!
                .SetValue(job, _repId);
        }
        else if (status == DeliveryStatus.Assigned)
        {
            job.Assign(_repId, "Test Rep", DateOnly.FromDateTime(DateTime.UtcNow), "op", "AgencyOperator");
        }
        else if (status == DeliveryStatus.Delivered || status == DeliveryStatus.Failed)
        {
            job.Assign(_repId, "Test Rep", DateOnly.FromDateTime(DateTime.UtcNow), "op", "AgencyOperator");
            job.ChangeStatus(DeliveryStatus.InTransit, "rep", "SalesRep");
            job.ChangeStatus(status, "rep", "SalesRep", status == DeliveryStatus.Failed ? "Road closed" : null);
        }
        else if (status == DeliveryStatus.Cancelled)
        {
            job.Assign(_repId, "Test Rep", DateOnly.FromDateTime(DateTime.UtcNow), "op", "AgencyOperator");
            job.ChangeStatus(DeliveryStatus.Cancelled, "op", "AgencyOperator");
        }

        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        _mockRepository.Verify(r => r.RecordLocationAsync(It.IsAny<DeliveryLocation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(90.0001, 79.8612, "latitude")]
    [InlineData(-90.0001, 79.8612, "latitude")]
    [InlineData(6.9271, 180.0001, "longitude")]
    [InlineData(6.9271, -180.0001, "longitude")]
    public async Task RecordLocation_WhenCoordinatesOutOfRange_ReturnsBadRequestNamingField(
        decimal latitude, decimal longitude, string expectedField)
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        problem.Errors.Should().ContainKey(expectedField);
    }

    [Fact]
    public async Task RecordLocation_WhenAccuracyNegative_ReturnsBadRequestNamingAccuracyMeters()
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            AccuracyMeters = -0.001,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        problem.Errors.Should().ContainKey("accuracyMeters");
    }

    [Fact]
    public async Task RecordLocation_WhenCapturedAtMoreThan2MinutesInFuture_ReturnsBadRequest()
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // 2 minutes and 1 second in the future (outside window)
        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            CapturedAt = _fixedNow.AddMinutes(2).AddSeconds(1)
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        problem.Errors.Should().ContainKey("capturedAt");
    }

    [Fact]
    public async Task RecordLocation_WhenCapturedAtOlderThan30Minutes_ReturnsBadRequest()
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // 30 minutes and 1 second in the past (outside window)
        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            CapturedAt = _fixedNow.AddMinutes(-30).AddSeconds(-1)
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        problem.Errors.Should().ContainKey("capturedAt");
    }

    [Fact]
    public async Task RecordLocation_ExactBoundaryValues_AcceptedWithNoContent()
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _mockRepository.Setup(r => r.RecordLocationAsync(It.IsAny<DeliveryLocation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Test boundary 1: exactly +2 minutes in future, lat 90, lon 180, accuracy 0
        var req1 = new RecordDeliveryLocationRequest
        {
            Latitude = 90.0m,
            Longitude = 180.0m,
            AccuracyMeters = 0.0,
            CapturedAt = _fixedNow.AddMinutes(2)
        };
        var res1 = await _controller.RecordLocation(job.DeliveryJobId, req1, CancellationToken.None);
        res1.Should().BeOfType<NoContentResult>();

        // Test boundary 2: exactly -30 minutes in past, lat -90, lon -180, accuracy null
        var req2 = new RecordDeliveryLocationRequest
        {
            Latitude = -90.0m,
            Longitude = -180.0m,
            AccuracyMeters = null,
            CapturedAt = _fixedNow.AddMinutes(-30)
        };
        var res2 = await _controller.RecordLocation(job.DeliveryJobId, req2, CancellationToken.None);
        res2.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RecordLocation_WhenValid_ReturnsNoContentAndRecordsLocation()
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _mockRepository.Setup(r => r.RecordLocationAsync(It.IsAny<DeliveryLocation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            AccuracyMeters = 12.5,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _mockRepository.Verify(r => r.RecordLocationAsync(It.Is<DeliveryLocation>(l =>
            l.DeliveryJobId == job.DeliveryJobId &&
            l.Latitude == 6.9271m &&
            l.Longitude == 79.8612m &&
            l.AccuracyMeters == 12.5 &&
            l.CapturedAt == _fixedNow.ToUniversalTime() &&
            l.RecordedByUserId == "rep-user-id"
        ), It.IsAny<CancellationToken>()), Times.Once);

        // No Kafka message or outbox written for location posts
        _mockOutboxWriter.Verify(w => w.Write(It.IsAny<OutboxMessage>()), Times.Never);
    }

    [Fact]
    public async Task RecordLocation_WhenJobLeavesInTransitConcurrently_ReturnsConflict()
    {
        var job = CreateInTransitJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _mockRepository.Setup(r => r.RecordLocationAsync(It.IsAny<DeliveryLocation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Job transitioned out of InTransit concurrently

        var request = new RecordDeliveryLocationRequest
        {
            Latitude = 6.9271m,
            Longitude = 79.8612m,
            CapturedAt = _fixedNow
        };

        var result = await _controller.RecordLocation(job.DeliveryJobId, request, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetLocation_WhenJobNotFound_ReturnsNotFound()
    {
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryJob)null!);

        var result = await _controller.GetLocation(jobId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("ShopOwner")]
    [InlineData("SalesRep")]
    [InlineData("AgencyOperator")]
    [InlineData("CompanyAdmin")]
    [InlineData("AreaManager")]
    public async Task GetLocation_WhenAllowedRoleInScope_ReturnsOkWithTrackingData(string role)
    {
        var job = CreateInTransitJob();
        job.SetShopCoordinates(6.9000m, 79.8500m);
        job.UpdateLatestLocation(6.9271m, 79.8612m, _fixedNow);

        SetUser(role);
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(
                job.AssignedRepId,
                job.AgencyId,
                job.ShopId,
                [job.ProvinceId],
                "Scoped User"));

        var result = await _controller.GetLocation(job.DeliveryJobId, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DeliveryTrackingResponse>().Subject;
        response.DeliveryJobId.Should().Be(job.DeliveryJobId);
        response.Status.Should().Be(DeliveryStatus.InTransit.ToString());
        response.Position.Should().NotBeNull();
        response.Position!.Latitude.Should().Be(6.9271m);
        response.Position.Longitude.Should().Be(79.8612m);
        response.Position.CapturedAt.Should().Be(_fixedNow);
        response.Shop.Should().NotBeNull();
        response.Shop!.Latitude.Should().Be(6.9000m);
        response.Shop.Longitude.Should().Be(79.8500m);
    }

    [Theory]
    [InlineData("ShopOwner")]
    [InlineData("SalesRep")]
    [InlineData("AgencyOperator")]
    [InlineData("AreaManager")]
    public async Task GetLocation_WhenOutOfScope_ReturnsNotFound(string role)
    {
        var job = CreateInTransitJob();
        SetUser(role);
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Caller scope has different IDs than the job
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                [Guid.NewGuid()],
                "Other User"));

        var result = await _controller.GetLocation(job.DeliveryJobId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLocation_WhenNoPositionReported_ReturnsPositionNull()
    {
        var job = CreateInTransitJob();
        job.SetShopCoordinates(6.9000m, 79.8500m);
        // No location reported yet: LastLatitude / Longitude are null

        SetUser("CompanyAdmin");
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, Array.Empty<Guid>(), "Admin"));

        var result = await _controller.GetLocation(job.DeliveryJobId, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DeliveryTrackingResponse>().Subject;
        response.Position.Should().BeNull();
        response.Shop.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLocation_WhenNoShopCoordinatesSnapshot_ReturnsShopNull()
    {
        var job = CreateInTransitJob();
        // ShopLatitude / Longitude are null
        job.UpdateLatestLocation(6.9271m, 79.8612m, _fixedNow);

        SetUser("CompanyAdmin");
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, Array.Empty<Guid>(), "Admin"));

        var result = await _controller.GetLocation(job.DeliveryJobId, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DeliveryTrackingResponse>().Subject;
        response.Shop.Should().BeNull();
        response.Position.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLocation_WhenJobNotInTransit_ReturnsStoredPositionWithoutStatusGate()
    {
        var job = CreateInTransitJob();
        job.SetShopCoordinates(6.9000m, 79.8500m);
        job.UpdateLatestLocation(6.9271m, 79.8612m, _fixedNow);
        job.ChangeStatus(DeliveryStatus.Delivered, "rep", "SalesRep");

        SetUser("CompanyAdmin");
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, Array.Empty<Guid>(), "Admin"));

        var result = await _controller.GetLocation(job.DeliveryJobId, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DeliveryTrackingResponse>().Subject;
        response.Status.Should().Be(DeliveryStatus.Delivered.ToString());
        response.Position.Should().NotBeNull();
        response.Position!.Latitude.Should().Be(6.9271m);
    }

    private void SetUser(string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "test-user-id"),
            new Claim("roles", role)
        ]));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }
}
