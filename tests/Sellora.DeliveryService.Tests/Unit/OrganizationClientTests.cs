using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Infrastructure.Dependencies;

namespace Sellora.DeliveryService.Tests.Unit;

public class OrganizationClientTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly OrganizationClient _client;

    public OrganizationClientTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("http://test.com")
        };
        _client = new OrganizationClient(_httpClient);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, object content)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(JsonSerializer.Serialize(content))
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    [Fact]
    public async Task ListSalesRepsAsync_WhenSuccessful_ReturnsSalesReps()
    {
        // Arrange
        var expectedReps = new List<SalesRepSummary>
        {
            new(Guid.NewGuid(), "Rep 1", "test@test.com", "Active", new TerritoryReference(Guid.NewGuid(), "T1", "Territory 1"))
        };
        SetupHttpResponse(HttpStatusCode.OK, expectedReps);

        // Act
        var result = await _client.ListSalesRepsAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedReps);
    }

    [Fact]
    public async Task ListSalesRepsAsync_WhenFailed_ThrowsDependencyRejectedException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.BadRequest, new { detail = "Error" });

        // Act
        var action = async () => await _client.ListSalesRepsAsync();

        // Assert
        await action.Should().ThrowAsync<DependencyRejectedException>();
    }

    [Fact]
    public async Task GetCallerScopeAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "");

        // Act
        var result = await _client.GetCallerScopeAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCallerScopeAsync_WhenSuccessful_ReturnsScope()
    {
        // Arrange
        var expectedScopeDto = new 
        {
            SalesRepId = Guid.NewGuid(),
            AgencyId = Guid.NewGuid(),
            ShopId = Guid.NewGuid(),
            ProvinceIds = new List<Guid> { Guid.NewGuid() },
            DisplayName = "Test User"
        };
        SetupHttpResponse(HttpStatusCode.OK, expectedScopeDto);

        // Act
        var result = await _client.GetCallerScopeAsync();

        // Assert
        result.Should().NotBeNull();
        result!.SalesRepId.Should().Be(expectedScopeDto.SalesRepId);
        result.AgencyId.Should().Be(expectedScopeDto.AgencyId);
        result.ShopId.Should().Be(expectedScopeDto.ShopId);
        result.ProvinceIds.Should().BeEquivalentTo(expectedScopeDto.ProvinceIds);
        result.DisplayName.Should().Be(expectedScopeDto.DisplayName);
    }

    [Fact]
    public async Task GetCallerScopeAsync_WhenFailed_ThrowsDependencyRejectedException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.BadRequest, new { detail = "Error" });

        // Act
        var action = async () => await _client.GetCallerScopeAsync();

        // Assert
        await action.Should().ThrowAsync<DependencyRejectedException>();
    }
}
