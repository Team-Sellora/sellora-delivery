using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace Sellora.DeliveryService.Api.Security;

/// <summary>
/// JWT bearer and certificate-validation wiring used by Program.cs.
/// </summary>
public static class AuthenticationSetup
{
    public const string AuthorityKey = "Jwt:Authority";
    public const string AudienceKey = "Jwt:Audience";
    public const string MetadataAddressKey = "Jwt:MetadataAddress";
    public const string RequireHttpsMetadataKey = "Jwt:RequireHttpsMetadata";

    private const string StagingSettingsFile = "appsettings.Staging.json";

    /// <summary>
    /// Loads appsettings.Staging.json when Jwt:Authority is unset or still the placeholder,
    /// so a host that forgot ASPNETCORE_ENVIRONMENT=Staging still gets the staging values.
    /// </summary>
    public static void ApplyStagingFallback(WebApplicationBuilder builder)
    {
        if (!IsPlaceholderAuthority(builder.Configuration[AuthorityKey]))
        {
            return;
        }

        var stagingJson = Path.Combine(builder.Environment.ContentRootPath, StagingSettingsFile);
        if (File.Exists(stagingJson))
        {
            builder.Configuration.AddJsonFile(StagingSettingsFile, optional: true, reloadOnChange: false);
        }
    }

    public static bool IsPlaceholderAuthority(string? authority) =>
        string.IsNullOrWhiteSpace(authority) || authority.Contains(".internal", StringComparison.OrdinalIgnoreCase);

    // "Jwt:Audience" can be a JSON array (Jwt__Audience__0, Jwt__Audience__1) or a single value (Jwt__Audience).
    public static string[] ReadAudiences(IConfiguration configuration) =>
        configuration.GetSection(AudienceKey).Get<string[]>()
            ?? new[] { configuration[AudienceKey] ?? string.Empty };

    public static bool ShouldRelaxCertificateValidation(WebApplicationBuilder builder, string? jwtAuthority)
    {
        var isDevelopmentOrStaging = builder.Environment.IsDevelopment() || builder.Environment.IsStaging();

        return isDevelopmentOrStaging
            || IsIpAddressHost(jwtAuthority)
            || !builder.Configuration.GetValue(RequireHttpsMetadataKey, true);
    }

    private static bool IsIpAddressHost(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && Uri.TryCreate(address, UriKind.Absolute, out var parsed)
        && parsed.HostNameType == UriHostNameType.IPv4;

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string[] audiences,
        bool relaxCertificateValidation)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration[AuthorityKey];
                options.RequireHttpsMetadata = configuration.GetValue(RequireHttpsMetadataKey, !relaxCertificateValidation);
                if (relaxCertificateValidation)
                {
                    options.RequireHttpsMetadata = false;
                    options.BackchannelHttpHandler = IdentityProviderBackchannel.CreateHandler(
                        configuration[MetadataAddressKey] ?? configuration[AuthorityKey]);
                }
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Log.Error(context.Exception, "JWT authentication failed: {Message}", context.Exception.Message);
                        return Task.CompletedTask;
                    }
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudiences = audiences,
                    ValidateLifetime = true,
                    RoleClaimType = "roles"
                };
            });

        return services;
    }

    /// <summary>
    /// On hosts that do not trust the gateway CA, lets the factory-built HTTP clients accept
    /// a failing certificate from the configured dependency and identity provider hosts only.
    /// </summary>
    public static IServiceCollection RelaxDependencyCertificateValidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var validator = DependencyCertificatePolicy.CreateValidator(new[]
        {
            configuration["Dependencies:Organization:BaseUrl"],
            configuration["Dependencies:Inventory:BaseUrl"],
            configuration[MetadataAddressKey],
            configuration[AuthorityKey]
        });

        services.ConfigureAll<HttpClientFactoryOptions>(options =>
        {
            options.HttpMessageHandlerBuilderActions.Add(b =>
            {
                if (b.PrimaryHandler is HttpClientHandler handler)
                {
                    handler.ServerCertificateCustomValidationCallback = validator;
                }
            });
        });

        return services;
    }
}
