namespace Sellora.DeliveryService.Application.Services;

public interface IDeliveryReferenceGenerator
{
    string Generate(DateTimeOffset at);
}
