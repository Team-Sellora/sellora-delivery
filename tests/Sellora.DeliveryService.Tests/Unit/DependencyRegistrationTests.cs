using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Infrastructure.Dependencies;

namespace Sellora.DeliveryService.Tests.Unit;

public class DependencyRegistrationTests
{
    [Fact]
    public void AddDeliveryDependencies_RegistersOrganizationClientSuccessfully()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Dependencies:Organization:BaseUrl", "http://test.com"},
                {"Dependencies:Organization:TimeoutSeconds", "5"},
                {"Dependencies:CircuitBreaker:FailureRatio", "0.5"},
                {"Dependencies:CircuitBreaker:MinimumThroughput", "10"},
                {"Dependencies:CircuitBreaker:SamplingDurationSeconds", "10"},
                {"Dependencies:CircuitBreaker:BreakDurationSeconds", "30"}
            })
            .Build();

        var services = new ServiceCollection();
        
        // Act
        services.AddDeliveryDependencies(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(nameof(IOrganizationClient));
        
        client.Should().NotBeNull();
        client.BaseAddress.Should().Be(new Uri("http://test.com/"));
    }

    [Fact]
    public void AddDeliveryDependencies_WithInvalidBaseUrl_ThrowsException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Dependencies:Organization:BaseUrl", "not-a-url"}
            })
            .Build();

        var services = new ServiceCollection();
        
        // Act
        var action = () => services.AddDeliveryDependencies(configuration).Services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IOrganizationClient));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Dependencies:Organization:BaseUrl must be an absolute URL (got 'not-a-url').");
    }
}
