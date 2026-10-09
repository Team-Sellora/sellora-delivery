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
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveriesControllerQueryTests
{
    private readonly Mock<IDeliveryJobRepository> _repoMock;
    private readonly Mock<IOrganizationClient> _orgClientMock;
    private readonly Mock<ITenantContext> _tenantMock;
    private readonly Mock<IOutboxWriter> _outboxMock;
    private readonly DeliveriesController _controller;

    public DeliveriesControllerQueryTests()
    {
        _repoMock = new Mock<IDeliveryJobRepository>();
        _orgClientMock = new Mock<IOrganizationClient>();
        _tenantMock = new Mock<ITenantContext>();
        _outboxMock = new Mock<IOutboxWriter>();

        _controller = new DeliveriesController(
            _repoMock.Object,
            _orgClientMock.Object,
            _outboxMock.Object);
    }

    private void SetUserRole(string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("sub", "test-user")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetDeliveries_ShopOwnerSuppliesOtherShopId_ReturnsForbid()
    {
        // Arrange
        SetUserRole(SelloraRoles.ShopOwner);
        var ownShopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();

        _orgClientMock.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, ownShopId, new List<Guid>(), "Test"));

        // Act
        var result = await _controller.GetDeliveries(null, null, null, null, otherShopId, null);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetDeliveries_SalesRepSuppliesOtherRepId_ReturnsForbid()
    {
        // Arrange
        SetUserRole(SelloraRoles.SalesRep);
        var ownRepId = Guid.NewGuid();
        var otherRepId = Guid.NewGuid();

        _orgClientMock.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(ownRepId, null, null, new List<Guid>(), "Test"));

        // Act
        var result = await _controller.GetDeliveries(null, null, null, otherRepId, null, null);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetDeliveries_ValidRequest_PassesCorrectQueryToRepo()
    {
        // Arrange
        SetUserRole(SelloraRoles.AgencyOperator);
        var agencyId = Guid.NewGuid();

        _orgClientMock.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, agencyId, null, new List<Guid>(), "Test"));

        _repoMock.Setup(r => r.ListAsync(It.IsAny<DeliveryJobQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DeliveryJob>(), 0));

        // Act
        var result = await _controller.GetDeliveries(
            new[] { "Pending" },
            null, null, null, null, null, false, 2, 10);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.ListAsync(It.Is<DeliveryJobQuery>(q => 
            q.Page == 2 && 
            q.PageSize == 10 && 
            q.Statuses != null && q.Statuses.Contains("Pending") &&
            q.ScopeAgencyId == agencyId &&
            q.IsCompanyAdmin == false
        ), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetDeliveryById_OutsideScope_ReturnsNotFound()
    {
        // Arrange
        SetUserRole(SelloraRoles.ShopOwner);
        var ownShopId = Guid.NewGuid();
        var otherShopId = Guid.NewGuid();

        _orgClientMock.Setup(c => c.GetCallerScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallerScope(null, null, ownShopId, new List<Guid>(), "Test"));

        var job = typeof(DeliveryJob).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new Type[0], null)?.Invoke(null) as DeliveryJob;
        
        typeof(DeliveryJob).GetProperty("ShopId")?.SetValue(job, otherShopId);
        
        _repoMock.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var result = await _controller.GetDeliveryById(Guid.NewGuid(), default);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }
}
