namespace Sellora.DeliveryService.Application.Common.Interfaces;

public interface ITenantScoped
{
    Guid CompanyId { get; }
}
