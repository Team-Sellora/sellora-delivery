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

    [Fact]
    public async Task GetShopCoordinatesAsync_WhenShopFound_ReturnsCoordinates()
    {
        // Arrange
        var shopId = Guid.NewGuid();
        var tree = new
        {
            CompanyId = Guid.NewGuid(),
            Name = "Company",
            Provinces = new[]
            {
                new
                {
                    ProvinceId = Guid.NewGuid(),
                    Code = "WP",
                    Name = "Western",
                    Agencies = new[]
                    {
                        new
                        {
                            AgencyId = Guid.NewGuid(),
                            Name = "Agency A",
                            Territories = new[]
                            {
                                new
                                {
                                    TerritoryId = Guid.NewGuid(),
                                    Code = "T1",
                                    Name = "Territory 1",
                                    Shops = new[]
                                    {
                                        new
                                        {
                                            ShopId = shopId,
                                            Latitude = 6.9271m,
                                            Longitude = 79.8612m
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, tree);

        // Act
        var result = await _client.GetShopCoordinatesAsync(shopId);

        // Assert
        result.Should().NotBeNull();
        result!.Latitude.Should().Be(6.9271m);
        result.Longitude.Should().Be(79.8612m);
    }

    [Fact]
    public async Task GetShopCoordinatesAsync_WhenNotFound404_ReturnsNull()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "");

        // Act
        var result = await _client.GetShopCoordinatesAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShopCoordinatesAsync_WhenShopNotInHierarchy_ReturnsNull()
    {
        // Arrange
        var tree = new
        {
            CompanyId = Guid.NewGuid(),
            Name = "Company",
            Provinces = new[]
            {
                new
                {
                    ProvinceId = Guid.NewGuid(),
                    Code = "WP",
                    Name = "Western",
                    Agencies = new[]
                    {
                        new
                        {
                            AgencyId = Guid.NewGuid(),
                            Name = "Agency A",
                            Territories = new[]
                            {
                                new
                                {
                                    TerritoryId = Guid.NewGuid(),
                                    Code = "T1",
                                    Name = "Territory 1",
                                    Shops = new[]
                                    {
                                        new
                                        {
                                            ShopId = Guid.NewGuid(),
                                            Latitude = 6.9271m,
                                            Longitude = 79.8612m
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, tree);

        // Act
        var result = await _client.GetShopCoordinatesAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShopCoordinatesAsync_WhenCoordinatesZero_ReturnsNull()
    {
        // Arrange
        var shopId = Guid.NewGuid();
        var tree = new
        {
            CompanyId = Guid.NewGuid(),
            Name = "Company",
            Provinces = new[]
            {
                new
                {
                    ProvinceId = Guid.NewGuid(),
                    Code = "WP",
                    Name = "Western",
                    Agencies = new[]
                    {
                        new
                        {
                            AgencyId = Guid.NewGuid(),
                            Name = "Agency A",
                            Territories = new[]
                            {
                                new
                                {
                                    TerritoryId = Guid.NewGuid(),
                                    Code = "T1",
                                    Name = "Territory 1",
                                    Shops = new[]
                                    {
                                        new
                                        {
                                            ShopId = shopId,
                                            Latitude = 0m,
                                            Longitude = 0m
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, tree);

        // Act
        var result = await _client.GetShopCoordinatesAsync(shopId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShopCoordinatesAsync_WhenDownstream500_ThrowsDependencyUnavailableException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, new { detail = "Internal Error" });

        // Act
        var action = async () => await _client.GetShopCoordinatesAsync(Guid.NewGuid());

        // Assert
        await action.Should().ThrowAsync<DependencyUnavailableException>();
    }
}
