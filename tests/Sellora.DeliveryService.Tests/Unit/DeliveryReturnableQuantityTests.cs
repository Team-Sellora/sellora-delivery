using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Sellora.DeliveryService.Api.Controllers;
using Sellora.DeliveryService.Api.Security;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryReturnableQuantityTests
{
    private readonly Mock<IDeliveryJobRepository> _deliveryRepoMock = new();
    private readonly Mock<IOrganizationClient> _orgClientMock = new();
    private readonly Mock<IOutboxWriter> _outboxWriterMock = new();
    private readonly Mock<IReturnRequestRepository> _returnRepoMock = new();
    private readonly DeliveriesController _controller;

    public DeliveryReturnableQuantityTests()
    {
        _controller = new DeliveriesController(
            _deliveryRepoMock.Object,
            _orgClientMock.Object,
            _outboxWriterMock.Object,
            _returnRepoMock.Object);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", "admin-1"),
            new Claim("roles", SelloraRoles.CompanyAdmin)
        }, "TestAuth");

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        _orgClientMock.Setup(o => o.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, null, [], "Admin"));
    }

    [Fact]
    public async Task GetDeliveryById_ComputesReturnableQuantity_DeductingApprovedAndPendingReturns()
    {
        var prodId1 = Guid.NewGuid();
        var prodId2 = Guid.NewGuid();

        var lines = new List<DeliveryJobLine>
        {
            new(Guid.Empty, Guid.NewGuid(), prodId1, "Product 1", 20, 100m, 2000m),
            new(Guid.Empty, Guid.NewGuid(), prodId2, "Product 2", 10, 50m, 500m)
        };

        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "DL-001", Guid.NewGuid(), "ORD-001", "ScheduledDelivery",
            Guid.NewGuid(), "Shop", "Owner", "email", Guid.NewGuid(), "Agency", "email",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2500m, "LKR", lines);

        _deliveryRepoMock.Setup(r => r.GetByIdWithDetailsAsync(job.DeliveryJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // For Product 1: 5 Requested, 10 Approved -> 15 returned/requested
        // For Product 2: 0 returned
        var returnedMap = new Dictionary<Guid, int>
        {
            [prodId1] = 15
        };

        _returnRepoMock.Setup(r => r.GetReturnedQuantitiesByJobIdAsync(job.DeliveryJobId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedMap);

        var result = await _controller.GetDeliveryById(job.DeliveryJobId, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DeliveryDetailResponse>().Subject;

        var line1 = response.Lines.First(l => l.ProductId == prodId1);
        line1.Quantity.Should().Be(20);
        line1.ReturnableQuantity.Should().Be(5); // 20 - 15 = 5

        var line2 = response.Lines.First(l => l.ProductId == prodId2);
        line2.Quantity.Should().Be(10);
        line2.ReturnableQuantity.Should().Be(10); // 10 - 0 = 10
    }
}
