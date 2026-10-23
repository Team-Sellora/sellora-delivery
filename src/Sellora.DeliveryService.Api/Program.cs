using Microsoft.EntityFrameworkCore;
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

    AuthenticationSetup.ApplyStagingFallback(builder);

    var jwtAuthority = builder.Configuration[AuthenticationSetup.AuthorityKey];
    // Same rule as Order, Inventory, Catalog, Organization and Notification:
    // accept every WSO2 client the web app may sign in with.
    var jwtAudiences = AuthenticationSetup.ReadAudiences(builder.Configuration);
    var jwtAudience = string.Join(", ", jwtAudiences);

    Log.Information(
        "Startup configuration: environment={Environment}, jwtAuthority={JwtAuthority}, jwtAudience={JwtAudience}",
        builder.Environment.EnvironmentName,
        jwtAuthority,
        jwtAudience);

    if (AuthenticationSetup.IsPlaceholderAuthority(jwtAuthority))
    {
        Log.Warning(
            "Jwt:Authority is unset or still the placeholder value. Every token will be rejected. " +
            "Set Jwt__Authority and Jwt__Audience, and ASPNETCORE_ENVIRONMENT=Staging on non-production hosts.");
    }

    var shouldRelaxCertificateValidation = AuthenticationSetup.ShouldRelaxCertificateValidation(builder, jwtAuthority);

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

    builder.Services.AddJwtAuthentication(builder.Configuration, jwtAudiences, shouldRelaxCertificateValidation);

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
        builder.Services.RelaxDependencyCertificateValidation(builder.Configuration);
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
