using System.Net.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Sellora.DeliveryService.Api.Security;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public sealed class AuthenticationSetupTests
{
    private static WebApplicationBuilder CreateBuilder(
        string environment,
        Dictionary<string, string?> settings,
        string? contentRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ContentRootPath = contentRoot ?? AppContext.BaseDirectory
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://wso2.sellora.internal/oauth2/token", true)]
    [InlineData("https://13.61.228.129:9443/oauth2/token", false)]
    public void Placeholder_authority_is_detected(string? authority, bool expected)
    {
        Assert.Equal(expected, AuthenticationSetup.IsPlaceholderAuthority(authority));
    }

    [Fact]
    public void Audiences_are_read_from_an_array()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Audience:0"] = "client-a",
            ["Jwt:Audience:1"] = "client-b"
        }).Build();

        Assert.Equal(["client-a", "client-b"], AuthenticationSetup.ReadAudiences(configuration));
    }

    [Fact]
    public void Audiences_fall_back_to_a_single_value_or_empty()
    {
        var single = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Audience"] = "client-a"
        }).Build();

        Assert.Equal(["client-a"], AuthenticationSetup.ReadAudiences(single));
        Assert.Equal([string.Empty], AuthenticationSetup.ReadAudiences(new ConfigurationBuilder().Build()));
    }

    [Theory]
    [InlineData("Development", "https://auth.example.org/", null, true)]
    [InlineData("Staging", "https://auth.example.org/", null, true)]
    [InlineData("Production", "https://13.61.228.129:9443/oauth2/token", null, true)]
    [InlineData("Production", "https://auth.example.org/", "false", true)]
    [InlineData("Production", "https://auth.example.org/", null, false)]
    [InlineData("Production", null, null, false)]
    public void Certificate_validation_is_relaxed_only_for_untrusted_hosts(
        string environment, string? authority, string? requireHttpsMetadata, bool expected)
    {
        var builder = CreateBuilder(environment, new Dictionary<string, string?>
        {
            [AuthenticationSetup.RequireHttpsMetadataKey] = requireHttpsMetadata
        });

        Assert.Equal(expected, AuthenticationSetup.ShouldRelaxCertificateValidation(builder, authority));
    }

    [Fact]
    public void Staging_fallback_is_skipped_when_a_real_authority_is_configured()
    {
        var builder = CreateBuilder("Production", new Dictionary<string, string?>
        {
            [AuthenticationSetup.AuthorityKey] = "https://auth.example.org/"
        });
        var sources = builder.Configuration.Sources.Count;

        AuthenticationSetup.ApplyStagingFallback(builder);

        Assert.Equal(sources, builder.Configuration.Sources.Count);
    }

    [Fact]
    public void Staging_fallback_is_skipped_when_the_staging_file_is_missing()
    {
        var emptyRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var builder = CreateBuilder("Production", new Dictionary<string, string?>(), emptyRoot);
            var sources = builder.Configuration.Sources.Count;

            AuthenticationSetup.ApplyStagingFallback(builder);

            Assert.Equal(sources, builder.Configuration.Sources.Count);
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

    [Fact]
    public void Staging_fallback_loads_the_staging_file_for_a_placeholder_authority()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "appsettings.Staging.json"),
                """{ "Jwt": { "Authority": "https://13.61.228.129:9443/oauth2/token" } }""");
            var builder = CreateBuilder("Production", new Dictionary<string, string?>
            {
                [AuthenticationSetup.AuthorityKey] = "https://wso2.sellora.internal/oauth2/token"
            }, root);

            AuthenticationSetup.ApplyStagingFallback(builder);

            Assert.Equal("https://13.61.228.129:9443/oauth2/token", builder.Configuration[AuthenticationSetup.AuthorityKey]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Jwt_bearer_is_configured_from_settings(bool relax)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AuthenticationSetup.AuthorityKey] = "https://13.61.228.129:9443/oauth2/token"
        }).Build();
        var services = new ServiceCollection();

        services.AddJwtAuthentication(configuration, ["client-a"], relax);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("https://13.61.228.129:9443/oauth2/token", options.Authority);
        Assert.Equal(!relax, options.RequireHttpsMetadata);
        Assert.Equal(relax, options.BackchannelHttpHandler is not null);
        Assert.Equal(["client-a"], options.TokenValidationParameters.ValidAudiences);
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void Dependency_clients_trust_a_failing_certificate_only_from_configured_hosts()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dependencies:Organization:BaseUrl"] = "https://13.61.228.129:8244/sellora-organization/1.0.0/"
        }).Build();
        var services = new ServiceCollection();
        services.AddHttpClient("organization");

        services.RelaxDependencyCertificateValidation(configuration);

        using var provider = services.BuildServiceProvider();
        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get("organization");
        var handlerBuilder = new StubHandlerBuilder();
        foreach (var action in factoryOptions.HttpMessageHandlerBuilderActions)
        {
            action(handlerBuilder);
        }

        var callback = ((HttpClientHandler)handlerBuilder.PrimaryHandler).ServerCertificateCustomValidationCallback;
        Assert.NotNull(callback);

        using var trusted = new HttpRequestMessage(HttpMethod.Get, "https://13.61.228.129:8244/sellora-organization/1.0.0/api/me/scope");
        using var other = new HttpRequestMessage(HttpMethod.Get, "https://example.org/");
        Assert.True(callback(trusted, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(callback(other, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    private sealed class StubHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();

        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
