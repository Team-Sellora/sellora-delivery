using FluentAssertions;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryJobTests
{
    private DeliveryJob CreateTestJob()
    {
        return DeliveryJob.CreatePending(
            Guid.NewGuid(), "REF-123", Guid.NewGuid(), "ORD-1", "Delivery",
            Guid.NewGuid(), "Shop", "Owner", "email@test.com",
            Guid.NewGuid(), "Agency", "agency@test.com",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1000m, "LKR", new List<DeliveryJobLine>());
    }

    [Fact]
    public void Assign_WhenStatusIsPending_UpdatesPropertiesAndAddsHistory()
    {
        // Arrange
        var job = CreateTestJob();
        var repId = Guid.NewGuid();
        var scheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var userId = "test-user-id";

        // Act
        job.Assign(repId, "Test Rep", scheduledDate, userId, "AgencyOperator");

        // Assert
        job.Status.Should().Be(DeliveryStatus.Assigned);
        job.AssignedRepId.Should().Be(repId);
        job.AssignedRepName.Should().Be("Test Rep");
        job.ScheduledDate.Should().Be(scheduledDate);

        // Check history
        job.StatusHistory.Should().ContainSingle();
        var history = job.StatusHistory.First();
        
        history.DeliveryJobId.Should().Be(job.DeliveryJobId);
        history.FromStatus.Should().Be(DeliveryStatus.Pending);
        history.ToStatus.Should().Be(DeliveryStatus.Assigned);
        history.ActorUserId.Should().Be(userId);
    }
}
