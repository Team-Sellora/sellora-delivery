using System.Net;
using FluentAssertions;
using Sellora.DeliveryService.Application.Dependencies;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Dependencies;
using Sellora.DeliveryService.Infrastructure.Tenancy;

namespace Sellora.DeliveryService.Tests.Unit;

public class InfrastructureResilienceTests
{
    [Fact]
    public void OutboxMessage_LeaseAndProcessedState_AreRecorded()
    {
        var companyId = Guid.NewGuid();
        var message = new OutboxMessage(companyId, "sellora.delivery.v1", "ORD-1", "DeliveryJobCreated", "{}");

        message.CompanyId.Should().Be(companyId);
        message.Topic.Should().Be("sellora.delivery.v1");
        message.ProcessedAt.Should().BeNull();
        message.AcquireLease("relay-1", TimeSpan.FromMinutes(1));
        message.LeaseId.Should().Be("relay-1");
        message.LeaseExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        message.MarkProcessed();
        message.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void SystemTenantContext_RequiresAnExplicitCompany()
    {
        var context = new SystemTenantContext();
        context.IsSet.Should().BeFalse();
        Action readBeforeSet = () => _ = context.CompanyId;
        readBeforeSet.Should().Throw<InvalidOperationException>();

        var companyId = Guid.NewGuid();
        context.SetCompanyId(companyId);
        context.IsSet.Should().BeTrue();
        context.CompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task DependencyHttp_ServerError_BecomesUnavailable()
    {
        using var http = ClientReturning(HttpStatusCode.ServiceUnavailable);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://organization.invalid/company");

        Func<Task> send = async () => await DependencyHttp.SendAsync(
            http, request, Dependency.Organization, CancellationToken.None);

        var error = await send.Should().ThrowAsync<DependencyUnavailableException>();
        error.Which.Dependency.Should().Be(Dependency.Organization);
    }

    [Fact]
    public async Task DependencyHttp_NetworkFailure_BecomesUnavailable()
    {
        using var http = new HttpClient(new StubHandler(() => throw new HttpRequestException("network down")));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://organization.invalid/company");

        Func<Task> send = async () => await DependencyHttp.SendAsync(
            http, request, Dependency.Organization, CancellationToken.None);

        var error = await send.Should().ThrowAsync<DependencyUnavailableException>();
        error.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task DependencyHttp_Success_ReturnsResponse()
    {
        using var http = ClientReturning(HttpStatusCode.OK);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://organization.invalid/company");

        using var response = await DependencyHttp.SendAsync(
            http, request, Dependency.Organization, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpClient ClientReturning(HttpStatusCode status) =>
        new(new StubHandler(() => new HttpResponseMessage(status)));

    private sealed class StubHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }
}
