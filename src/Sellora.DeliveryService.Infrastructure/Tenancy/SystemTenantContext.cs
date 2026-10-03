using Sellora.DeliveryService.Application.Common.Interfaces;

namespace Sellora.DeliveryService.Infrastructure.Tenancy;

/// <summary>
/// Thread-local system tenant context used by background services.
/// Each consumer message sets the company scope explicitly from the event payload;
/// this is intentionally not registered as scoped (it's a per-service-scope object).
/// </summary>
public class SystemTenantContext : ISystemTenantContext, ITenantContext
{
    private Guid _companyId;

    public bool IsSet => _companyId != Guid.Empty;

    public Guid CompanyId => _companyId == Guid.Empty
        ? throw new InvalidOperationException("Company scope has not been set on this SystemTenantContext.")
        : _companyId;

    public void SetCompanyId(Guid companyId) => _companyId = companyId;
}
