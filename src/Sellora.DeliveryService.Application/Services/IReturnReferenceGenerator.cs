namespace Sellora.DeliveryService.Application.Services;

public interface IReturnReferenceGenerator
{
    string Generate(DateTimeOffset at);
}
