using Microsoft.Extensions.Configuration;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

/// <summary>
/// appsettings.json ships placeholder Jwt and dependency values. The Staging layer must replace
/// all of them, otherwise every token is rejected and every dependency call fails.
/// </summary>
public sealed class StagingConfigurationTests
{
    private static IConfiguration LoadStaging()
    {
        var apiDirectory = FindApiDirectory();

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.Staging.json"), optional: false)
            .Build();
    }

    private static string FindApiDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Sellora.DeliveryService.Api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/Sellora.DeliveryService.Api from the test output directory.");
    }

    [Fact]
    public void Staging_replaces_the_placeholder_jwt_values()
    {
        var jwt = LoadStaging().GetSection("Jwt");

        Assert.StartsWith("https://", jwt["Authority"]);
        Assert.DoesNotContain(".internal", jwt["Authority"], StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(jwt["Audience"]));
        Assert.NotEqual("sellora-delivery", jwt["Audience"]);
    }

    [Theory]
    [InlineData("Organization")]
    [InlineData("Inventory")]
    public void Staging_points_dependencies_at_the_gateway(string dependency)
    {
        var section = LoadStaging().GetSection($"Dependencies:{dependency}");

        Assert.StartsWith("https://", section["BaseUrl"]);
        Assert.EndsWith("/", section["BaseUrl"]);
        Assert.True(section.GetValue<int>("TimeoutSeconds") > 0);
    }

    [Fact]
    public void Staging_keeps_the_circuit_breaker_settings_from_the_base_file()
    {
        var breaker = LoadStaging().GetSection("Dependencies:CircuitBreaker");

        Assert.True(breaker.GetValue<double>("FailureRatio") > 0);
        Assert.True(breaker.GetValue<int>("BreakDurationSeconds") > 0);
    }

    [Fact]
    public void Fallback_loads_staging_when_authority_is_placeholder()
    {
        var apiDirectory = FindApiDirectory();
        var baseConfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.json"), optional: false)
            .Build();

        var initialAuthority = baseConfig["Jwt:Authority"];
        Assert.Contains(".internal", initialAuthority, StringComparison.OrdinalIgnoreCase);

        var fallbackConfig = new ConfigurationBuilder()
            .AddConfiguration(baseConfig)
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.Staging.json"), optional: true)
            .Build();

        Assert.Equal("https://13.61.228.129:9443/oauth2/token", fallbackConfig["Jwt:Authority"]);
        Assert.Equal("gJnSKbGIur_fls3FPgZg0Dim1p4a", fallbackConfig["Jwt:Audience"]);
    }
}
