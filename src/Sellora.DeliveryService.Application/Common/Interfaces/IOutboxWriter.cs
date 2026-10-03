using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Application.Common.Interfaces;

public interface IOutboxWriter
{
    void Write(OutboxMessage message);
}
