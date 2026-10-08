using FluentAssertions;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryTransitionsTests
{
    public static IEnumerable<object[]> GetAllTransitions()
    {
        var statuses = Enum.GetValues<DeliveryStatus>();
        foreach (var from in statuses)
        {
            foreach (var to in statuses)
            {
                yield return new object[] { from, to };
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetAllTransitions))]
    public void IsAllowed_ReturnsExpectedResult_ForEveryPair(DeliveryStatus from, DeliveryStatus to)
    {
        // Expected allowed transitions explicitly mapped in the test
        var isExpectedAllowed = (from, to) switch
        {
            (DeliveryStatus.Pending, DeliveryStatus.Assigned) => true,
            
            (DeliveryStatus.Assigned, DeliveryStatus.InTransit) => true,
            
            (DeliveryStatus.InTransit, DeliveryStatus.Delivered) => true,
            (DeliveryStatus.InTransit, DeliveryStatus.Failed) => true,
            
            (DeliveryStatus.Pending, DeliveryStatus.Cancelled) => true,
            (DeliveryStatus.Assigned, DeliveryStatus.Cancelled) => true,
            (DeliveryStatus.InTransit, DeliveryStatus.Cancelled) => true,
            
            _ => false
        };

        var result = DeliveryTransitions.IsAllowed(from, to);

        result.Should().Be(isExpectedAllowed, $"Transition from {from} to {to} should be {(isExpectedAllowed ? "allowed" : "denied")}");
    }
}
