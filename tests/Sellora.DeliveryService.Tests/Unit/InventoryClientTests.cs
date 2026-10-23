using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Infrastructure.Dependencies;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class InventoryClientTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly InventoryClient _client;

    public InventoryClientTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("http://test-inventory.com")
        };
        _client = new InventoryClient(_httpClient);
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
    public async Task GetInventoryOwnersAsync_WhenSuccessful_ReturnsOwners()
    {
        var expected = new List<InventoryOwnerSummary>
        {
            new(Guid.NewGuid(), "Agency", Guid.NewGuid(), "Agency Stock"),
            new(Guid.NewGuid(), "Company", Guid.NewGuid(), "Central Warehouse")
        };
        SetupHttpResponse(HttpStatusCode.OK, expected);

        var result = await _client.GetInventoryOwnersAsync();

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetInventoryOwnersAsync_WhenNotFound_ReturnsEmptyList()
    {
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        var result = await _client.GetInventoryOwnersAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInventoryOwnersAsync_WhenBadRequest_ThrowsDependencyRejectedException()
    {
        SetupHttpResponse(HttpStatusCode.BadRequest, new { title = "Bad Request" });

        var act = () => _client.GetInventoryOwnersAsync();

        var ex = await act.Should().ThrowAsync<DependencyRejectedException>();
        ex.Which.Dependency.Should().Be(Dependency.Inventory);
        ex.Which.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAgencyInventoryOwnerAsync_WhenAgencyMatches_ReturnsOwner()
    {
        var agencyId = Guid.NewGuid();
        var matchingOwner = new InventoryOwnerSummary(Guid.NewGuid(), "Agency", agencyId, "Agency Stock");
        var otherOwner = new InventoryOwnerSummary(Guid.NewGuid(), "Agency", Guid.NewGuid(), "Other Agency");
        SetupHttpResponse(HttpStatusCode.OK, new List<InventoryOwnerSummary> { matchingOwner, otherOwner });

        var result = await _client.GetAgencyInventoryOwnerAsync(agencyId);

        result.Should().NotBeNull();
        result!.InventoryOwnerId.Should().Be(matchingOwner.InventoryOwnerId);
    }

    [Fact]
    public async Task GetAgencyInventoryOwnerAsync_WhenAgencyNotFound_ReturnsNull()
    {
        var agencyId = Guid.NewGuid();
        var otherOwner = new InventoryOwnerSummary(Guid.NewGuid(), "Agency", Guid.NewGuid(), "Other Agency");
        SetupHttpResponse(HttpStatusCode.OK, new List<InventoryOwnerSummary> { otherOwner });

        var result = await _client.GetAgencyInventoryOwnerAsync(agencyId);

        result.Should().BeNull();
    }
}
