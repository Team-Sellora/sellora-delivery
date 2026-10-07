using Sellora.DeliveryService.Application.Common.Interfaces;

namespace Sellora.DeliveryService.Api.Tenancy;

internal class HttpTenantContext(IHttpContextAccessor accessor, ISystemTenantContext systemContext) : ITenantContext
{
    public Guid CompanyId
    {
        get
        {
            if (systemContext.IsSet)
            {
                return systemContext.CompanyId;
            }

            var claim = accessor.HttpContext?.User.FindFirst("companyId")?.Value
                     ?? accessor.HttpContext?.User.FindFirst("company_id")?.Value;
            if (Guid.TryParse(claim, out var id))
            {
                return id;
            }

            // At startup (EF Core model creation) there is no HTTP context and no system context set.
            // Returning Guid.Empty allows the global query filter to be built without throwing.
            return Guid.Empty;
        }
    }
}
