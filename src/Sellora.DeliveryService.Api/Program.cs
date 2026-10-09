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

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

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
            options.RequireHttpsMetadata = false;
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

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "sellora-delivery terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
    protected Program() { }
}
