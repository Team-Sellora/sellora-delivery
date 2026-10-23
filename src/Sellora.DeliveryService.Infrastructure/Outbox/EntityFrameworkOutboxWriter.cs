using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Persistence;

namespace Sellora.DeliveryService.Infrastructure.Outbox;

internal class EntityFrameworkOutboxWriter(DeliveryDbContext db) : IOutboxWriter
{
    public void Write(OutboxMessage message)
    {
        db.OutboxMessages.Add(message);
    }
}
