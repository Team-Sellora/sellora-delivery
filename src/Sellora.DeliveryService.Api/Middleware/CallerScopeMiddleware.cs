using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Infrastructure.Tenancy;

namespace Sellora.DeliveryService.Api.Middleware;

public class CallerScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // Tenant context is already resolved from JWT by HttpTenantContext.
        // This middleware exists as the extension point for additional caller-scope
        // enrichment (e.g. pushing CompanyId into Serilog context).
        using var scope = Serilog.Context.LogContext.PushProperty(
            "CompanyId",
            context.User.FindFirst("company_id")?.Value ?? "unknown");

        await next(context);
    }
}
