using System.Collections.Generic;

namespace Sellora.DeliveryService.Domain.Enums;

public static class DeliveryTransitions
{
    private static readonly HashSet<(DeliveryStatus From, DeliveryStatus To)> AllowedTransitions = new()
    {
        (DeliveryStatus.Pending, DeliveryStatus.Assigned),
        
        (DeliveryStatus.Assigned, DeliveryStatus.InTransit),
        
        (DeliveryStatus.InTransit, DeliveryStatus.Delivered),
        (DeliveryStatus.InTransit, DeliveryStatus.Failed),
        
        (DeliveryStatus.Pending, DeliveryStatus.Cancelled),
        (DeliveryStatus.Assigned, DeliveryStatus.Cancelled),
        (DeliveryStatus.InTransit, DeliveryStatus.Cancelled)
    };

    public static bool IsAllowed(DeliveryStatus from, DeliveryStatus to)
    {
        return AllowedTransitions.Contains((from, to));
    }
}
