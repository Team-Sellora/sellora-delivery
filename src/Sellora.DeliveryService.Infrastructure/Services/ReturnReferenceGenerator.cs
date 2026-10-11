using System.Security.Cryptography;
using Sellora.DeliveryService.Application.Services;

namespace Sellora.DeliveryService.Infrastructure.Services;

internal class ReturnReferenceGenerator : IReturnReferenceGenerator
{
    public string Generate(DateTimeOffset at)
    {
        var datePart = at.ToString("yyMMdd");
        var random = RandomNumberGenerator.GetBytes(3);
        var suffix = Convert.ToHexString(random);
        return $"RT-{datePart}-{suffix}";
    }
}
