using System.Net.Security;
using Sellora.DeliveryService.Api.Security;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

/// <summary>The relaxed dependency clients trust a failing certificate only from a configured dependency host.</summary>
public sealed class DependencyCertificatePolicyTests
{
    private static readonly Uri[] Gateway = [new("https://13.61.228.129:8244/sellora-organization/1.0.0/")];

    [Fact]
    public void A_valid_certificate_is_always_trusted()
    {
        Assert.True(DependencyCertificatePolicy.IsTrusted(
            new Uri("https://example.org/"), SslPolicyErrors.None, []));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    public void A_failing_certificate_is_trusted_from_a_dependency_host(SslPolicyErrors errors)
    {
        Assert.True(DependencyCertificatePolicy.IsTrusted(
            new Uri("https://13.61.228.129:8244/sellora-inventory/1.0.0/api/inventory-owners"), errors, Gateway));
    }

    [Theory]
    [InlineData("https://example.org/api/sales-reps")]
    [InlineData("https://13.61.228.129:9999/api/sales-reps")]
    public void A_failing_certificate_is_refused_from_any_other_host(string requestUri)
    {
        Assert.False(DependencyCertificatePolicy.IsTrusted(
            new Uri(requestUri), SslPolicyErrors.RemoteCertificateChainErrors, Gateway));
    }

    [Fact]
    public void A_failing_certificate_is_refused_without_a_request_uri()
    {
        Assert.False(DependencyCertificatePolicy.IsTrusted(
            null, SslPolicyErrors.RemoteCertificateChainErrors, Gateway));
    }

    [Fact]
    public void The_validator_ignores_unset_and_invalid_addresses()
    {
        var validator = DependencyCertificatePolicy.CreateValidator(
            [null, "not a url", "https://13.61.228.129:8244/sellora-organization/1.0.0/"]);

        using var trusted = new HttpRequestMessage(HttpMethod.Get, "https://13.61.228.129:8244/sellora-organization/1.0.0/api/me/scope");
        using var other = new HttpRequestMessage(HttpMethod.Get, "https://example.org/");

        Assert.True(validator(trusted, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(validator(other, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(validator(other, null, null, SslPolicyErrors.None));
    }
}
