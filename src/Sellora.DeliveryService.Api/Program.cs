using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Sellora.DeliveryService.Api.Http;
using Sellora.DeliveryService.Api.Middleware;
using Sellora.DeliveryService.Api.Security;
using Sellora.DeliveryService.Api.Tenancy;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Infrastructure;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Tenancy;
using Sellora.DeliveryService.Infrastructure.Dependencies;
using Sellora.DeliveryService.Application.Dependencies;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var initialAuthority = builder.Configuration["Jwt:Authority"];
    if (string.IsNullOrWhiteSpace(initialAuthority) || initialAuthority.Contains(".internal", StringComparison.OrdinalIgnoreCase))
    {
        var stagingJson = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Staging.json");
        if (File.Exists(stagingJson))
        {
            builder.Configuration.AddJsonFile("appsettings.Staging.json", optional: true, reloadOnChange: false);
        }
    }

    var jwtAuthority = builder.Configuration["Jwt:Authority"];
    var jwtAudience = builder.Configuration["Jwt:Audience"];

    Log.Information(
        "Startup configuration: environment={Environment}, jwtAuthority={JwtAuthority}, jwtAudience={JwtAudience}",
        builder.Environment.EnvironmentName,
        jwtAuthority,
        jwtAudience);

    if (string.IsNullOrWhiteSpace(jwtAuthority) || jwtAuthority.Contains(".internal", StringComparison.OrdinalIgnoreCase))
    {
        Log.Warning(
            "Jwt:Authority is unset or still the placeholder value. Every token will be rejected. " +
            "Set Jwt__Authority and Jwt__Audience, and ASPNETCORE_ENVIRONMENT=Staging on non-production hosts.");
    }

    var isDevelopmentOrStaging = builder.Environment.IsDevelopment() || builder.Environment.IsStaging();
    var isIpOrUntrustedHost = !string.IsNullOrWhiteSpace(jwtAuthority) &&
        (jwtAuthority.Contains("13.61.228.129") || (Uri.TryCreate(jwtAuthority, UriKind.Absolute, out var parsedAuth) && parsedAuth.HostNameType == UriHostNameType.IPv4));
    var shouldRelaxCertificateValidation = isDevelopmentOrStaging
        || isIpOrUntrustedHost
        || !builder.Configuration.GetValue("Jwt:RequireHttpsMetadata", true);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddHttpContextAccessor();

    // HTTP requests resolve ITenantContext from the JWT via HttpTenantContext.
    // Background service scopes (Kafka consumer, outbox relay) resolve SystemTenantContext
    // and call SetCompanyId explicitly from the event payload before any DB access.
    builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
    builder.Services.AddScoped<ISystemTenantContext, SystemTenantContext>();

    builder.Services.AddControllers();

    builder.Services.AddTransient<ForwardBearerTokenHandler>();
    builder.Services.AddTransient<ForwardCorrelationIdHandler>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Jwt:Authority"];
            options.Audience = builder.Configuration["Jwt:Audience"];
            options.RequireHttpsMetadata = builder.Configuration.GetValue("Jwt:RequireHttpsMetadata", !shouldRelaxCertificateValidation);
            if (shouldRelaxCertificateValidation)
            {
                options.RequireHttpsMetadata = false;
                options.BackchannelHttpHandler = IdentityProviderBackchannel.CreateHandler(
                    builder.Configuration["Jwt:MetadataAddress"] ?? builder.Configuration["Jwt:Authority"]);
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
                ValidateLifetime = true,
                RoleClaimType = "roles"
            };
        });

    builder.Services.AddAuthorization(RolePolicies.Register);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend",
            policy =>
            {
                policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
    });

    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddDeliveryDependencies(builder.Configuration)
        .AddHttpMessageHandler<ForwardBearerTokenHandler>()
        .AddHttpMessageHandler<ForwardCorrelationIdHandler>();

    builder.Services.AddHttpClient<IInventoryClient, InventoryClient>()
        .AddHttpMessageHandler<ForwardBearerTokenHandler>()
        .AddHttpMessageHandler<ForwardCorrelationIdHandler>();

    if (shouldRelaxCertificateValidation)
    {
        builder.Services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(options =>
        {
            options.HttpMessageHandlerBuilderActions.Add(b =>
            {
                if (b.PrimaryHandler is HttpClientHandler handler)
                {
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
            });
        });
    }

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors("AllowFrontend");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<CallerScopeMiddleware>();

    app.MapControllers();

    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        service = "sellora-delivery",
        revision = Environment.GetEnvironmentVariable("BUILD_COMMIT_SHA") ?? "unknown"
    }))
        .AllowAnonymous();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "sellora-delivery terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program
{
    protected Program() { }
}
