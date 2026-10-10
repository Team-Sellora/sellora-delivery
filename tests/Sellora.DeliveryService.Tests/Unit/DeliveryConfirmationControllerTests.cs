using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sellora.DeliveryService.Api.Controllers;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using System.Security.Claims;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryConfirmationControllerTests
{
    private readonly Mock<IDeliveryJobRepository> _mockRepository;
    private readonly Mock<IOrganizationClient> _mockOrganizationClient;
    private readonly Mock<IOutboxWriter> _mockOutboxWriter;
    private readonly DeliveriesController _controller;
    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _salesRepId = Guid.NewGuid();
    private readonly Guid _provinceId = Guid.NewGuid();

    public DeliveryConfirmationControllerTests()
    {
        _mockRepository = new Mock<IDeliveryJobRepository>();
        _mockOrganizationClient = new Mock<IOrganizationClient>();
        _mockOutboxWriter = new Mock<IOutboxWriter>();

        _controller = new DeliveriesController(
            _mockRepository.Object,
            _mockOrganizationClient.Object,
            _mockOutboxWriter.Object);

        SetupUserRole("ShopOwner", "user-shop-owner");

        _mockOrganizationClient.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(_salesRepId, _agencyId, _shopId, [_provinceId], "user-shop-owner"));
    }

    private void SetupUserRole(string role, string userId = "test-user")
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", userId),
            new Claim("roles", role)
        ]));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = user,
                Items = { ["CorrelationId"] = "corr-123" }
            }
        };
    }

    private DeliveryJob CreateJob(
        DeliveryStatus status = DeliveryStatus.Delivered,
        string fulfilmentType = "Delivery",
        Guid? shopId = null)
    {
        if (status == DeliveryStatus.Delivered)
        {
            return DeliveryJob.CreateDelivered(
                Guid.NewGuid(), "DEL-101", Guid.NewGuid(), "ORD-101", fulfilmentType,
                shopId ?? _shopId, "Test Shop", "Owner", "owner@test.com",
                _agencyId, "Test Agency", "agency@test.com",
                Guid.NewGuid(), _provinceId, _salesRepId,
                500m, "LKR", DateTimeOffset.UtcNow, new List<DeliveryJobLine>(), "test-user");
        }

        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "DEL-101", Guid.NewGuid(), "ORD-101", fulfilmentType,
            shopId ?? _shopId, "Test Shop", "Owner", "owner@test.com",
            _agencyId, "Test Agency", "agency@test.com",
            Guid.NewGuid(), _provinceId, Guid.NewGuid(),
            500m, "LKR", new List<DeliveryJobLine>()
        );

        if (status == DeliveryStatus.Assigned)
        {
            job.Assign(_salesRepId, "Rep Name", DateOnly.FromDateTime(DateTime.UtcNow), "test-user", "AgencyOperator");
        }

        return job;
    }

    [Fact]
    public async Task ConfirmDelivery_WhenUserNotShopOwnerOrAdmin_ReturnsForbid()
    {
        SetupUserRole("SalesRep");

        var result = await _controller.ConfirmDelivery(Guid.NewGuid(), new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenJobNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((DeliveryJob?)null);

        var result = await _controller.ConfirmDelivery(id, new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenShopIdMismatch_ReturnsForbid()
    {
        var otherShopId = Guid.NewGuid();
        var job = CreateJob(shopId: otherShopId);
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenJobNotDelivered_ReturnsConflict()
    {
        var job = CreateJob(status: DeliveryStatus.Assigned);
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenImmediateCashSale_ReturnsConflict()
    {
        var job = CreateJob(status: DeliveryStatus.Delivered, fulfilmentType: "ImmediateCashSale");
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenOutcomeIsInvalid_ReturnsBadRequest()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Unknown" }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenDisputedWithoutNotes_ReturnsBadRequest()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Disputed", Notes = "   " }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenNotesExceedLimit_ReturnsBadRequest()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var longNotes = new string('a', 1001);
        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Disputed", Notes = longNotes }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenSignatureUrlExceedLimit_ReturnsBadRequest()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var longUrl = new string('x', 2001);
        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Confirmed", SignatureUrl = longUrl }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenExistingConfirmationMatches_ReturnsOkIdempotent()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var existing = DeliveryConfirmation.Create(job.DeliveryJobId, job.CompanyId, ConfirmationType.Confirmed, "Existing note", "http://sig.png");
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dto = ok.Value as ConfirmationStateDto;
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Confirmed");
    }

    [Fact]
    public async Task ConfirmDelivery_WhenExistingConfirmationDiffers_ReturnsConflict()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var existing = DeliveryConfirmation.Create(job.DeliveryJobId, job.CompanyId, ConfirmationType.Confirmed, "Confirmed previously", null);
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Disputed", Notes = "Now dispute" }, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task ConfirmDelivery_WhenValidConfirmed_AddsConfirmationAndWritesOutbox()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync((DeliveryConfirmation?)null);

        var request = new DeliveryConfirmationRequest
        {
            Outcome = "Confirmed",
            Notes = "All packages intact",
            SignatureUrl = "https://signatures.sellora.com/sig.png"
        };

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dto = ok.Value as ConfirmationStateDto;
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Confirmed");
        dto.Notes.Should().Be("All packages intact");

        _mockRepository.Verify(r => r.AddConfirmationAsync(It.Is<DeliveryConfirmation>(c => c.Type == ConfirmationType.Confirmed), It.IsAny<CancellationToken>()), Times.Once);
        _mockOutboxWriter.Verify(w => w.Write(It.Is<OutboxMessage>(m => m.EventType == "DeliveryConfirmed")), Times.Once);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmDelivery_WhenValidDisputed_AddsConfirmationAndWritesOutbox()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync((DeliveryConfirmation?)null);

        var request = new DeliveryConfirmationRequest
        {
            Outcome = "Disputed",
            Notes = "Broken bottles in box"
        };

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dto = ok.Value as ConfirmationStateDto;
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Disputed");

        _mockRepository.Verify(r => r.AddConfirmationAsync(It.Is<DeliveryConfirmation>(c => c.Type == ConfirmationType.Disputed), It.IsAny<CancellationToken>()), Times.Once);
        _mockOutboxWriter.Verify(w => w.Write(It.Is<OutboxMessage>(m => m.EventType == "DeliveryDisputed")), Times.Once);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmDelivery_WhenDbUpdateExceptionOccurs_ReturnsConflict()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync((DeliveryConfirmation?)null);
        _mockRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Duplicate key", new Exception()));

        var result = await _controller.ConfirmDelivery(job.DeliveryJobId, new DeliveryConfirmationRequest { Outcome = "Confirmed" }, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetDeliveryById_WhenConfirmationExists_MapsConfirmationState()
    {
        var job = CreateJob();
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var existing = DeliveryConfirmation.Create(job.DeliveryJobId, job.CompanyId, ConfirmationType.Confirmed, "Received", "http://sig.png");
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _controller.GetDeliveryById(job.DeliveryJobId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dto = ok.Value as DeliveryDetailResponse;
        dto.Should().NotBeNull();
        var conf = dto!.Confirmation as ConfirmationStateDto;
        conf.Should().NotBeNull();
        conf!.State.Should().Be("Confirmed");
        conf.Note.Should().Be("Received");
    }

    [Fact]
    public async Task GetDeliveryById_WhenDeliveredWithoutConfirmation_MapsAwaitingState()
    {
        var job = CreateJob(status: DeliveryStatus.Delivered);
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _mockRepository.Setup(r => r.GetConfirmationByJobIdAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryConfirmation?)null);

        var result = await _controller.GetDeliveryById(job.DeliveryJobId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dto = ok.Value as DeliveryDetailResponse;
        dto.Should().NotBeNull();
        var conf = dto!.Confirmation as ConfirmationStateDto;
        conf.Should().NotBeNull();
        conf!.State.Should().Be("Awaiting");
    }

    [Fact]
    public async Task GetDeliveryById_WhenImmediateCashSale_ConfirmationIsNull()
    {
        var job = CreateJob(status: DeliveryStatus.Delivered, fulfilmentType: "ImmediateCashSale");
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        var result = await _controller.GetDeliveryById(job.DeliveryJobId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dto = ok.Value as DeliveryDetailResponse;
        dto.Should().NotBeNull();
        dto!.Confirmation.Should().BeNull();
    }

    [Fact]
    public async Task GetDeliveryById_AccessControl_ScopesAreEnforced()
    {
        var otherShopJob = CreateJob(shopId: Guid.NewGuid());
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(otherShopJob.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(otherShopJob);

        var result = await _controller.GetDeliveryById(otherShopJob.DeliveryJobId, CancellationToken.None);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetDeliveryById_AccessControl_AdminBypassesScope()
    {
        SetupUserRole("SystemAdmin");
        var otherShopJob = CreateJob(shopId: Guid.NewGuid());
        _mockRepository.Setup(r => r.GetByIdWithDetailsAsync(otherShopJob.DeliveryJobId, It.IsAny<CancellationToken>())).ReturnsAsync(otherShopJob);

        var result = await _controller.GetDeliveryById(otherShopJob.DeliveryJobId, CancellationToken.None);
        result.Should().BeOfType<OkObjectResult>();
    }
}
