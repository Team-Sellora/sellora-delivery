namespace Sellora.DeliveryService.Application.Common.Interfaces;

/// <summary>
/// Elevated context used by background services (e.g. Kafka consumers, outbox relay)
/// that operate outside of an HTTP request and therefore have no per-request tenant.
/// The consumer sets the company scope explicitly from the event payload.
/// </summary>
public interface ISystemTenantContext
{
    void SetCompanyId(Guid companyId);
    bool IsSet { get; }
    Guid CompanyId { get; }
}
