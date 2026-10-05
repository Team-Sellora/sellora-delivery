using System.Net;
using FluentAssertions;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Infrastructure.Dependencies;

namespace Sellora.DeliveryService.Tests.Unit;

public class DependencyExceptionTests
{
    [Fact]
    public void DependencyRejectedException_WithDetail_SetsPropertiesCorrectly()
    {
        var ex = new DependencyRejectedException(Dependency.Organization, HttpStatusCode.BadRequest, "Bad Request");
        ex.Dependency.Should().Be(Dependency.Organization);
        ex.Status.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Be("Organization rejected the request (HTTP 400): Bad Request");
    }

    [Fact]
    public void DependencyRejectedException_WithoutDetail_SetsPropertiesCorrectly()
    {
        var ex = new DependencyRejectedException(Dependency.Organization, HttpStatusCode.Forbidden, "");
        ex.Dependency.Should().Be(Dependency.Organization);
        ex.Status.Should().Be(HttpStatusCode.Forbidden);
        ex.Message.Should().Be("Organization rejected the request (HTTP 403).");
    }

    [Fact]
    public void DependencyUnavailableException_WithInnerException_SetsPropertiesCorrectly()
    {
        var inner = new Exception("Inner");
        var ex = new DependencyUnavailableException(Dependency.Organization, inner);
        ex.Dependency.Should().Be(Dependency.Organization);
        ex.InnerException.Should().Be(inner);
        ex.Message.Should().Be("Organization is unavailable.");
    }

    [Fact]
    public void DependencyUnavailableException_WithoutInnerException_SetsPropertiesCorrectly()
    {
        var ex = new DependencyUnavailableException(Dependency.Organization);
        ex.Dependency.Should().Be(Dependency.Organization);
        ex.InnerException.Should().BeNull();
        ex.Message.Should().Be("Organization is unavailable.");
    }
}
