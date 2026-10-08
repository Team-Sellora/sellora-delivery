using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Domain.Enums;
using Sellora.DeliveryService.Infrastructure.Persistence;

namespace Sellora.DeliveryService.Tests.Unit;

public class DeliveryDbContextTests
{
    private DeliveryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        return new DeliveryDbContext(options, null);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenModifyingDeliveryStatusHistory_ThrowsInvalidOperationException()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        
        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "REF", Guid.NewGuid(), "ORD", "Type",
            Guid.NewGuid(), "Shop", "Owner", "test@test.com",
            Guid.NewGuid(), "Agency", "test@test.com",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "LKR", new List<DeliveryJobLine>());
            
        job.Assign(Guid.NewGuid(), "Rep", DateOnly.FromDateTime(DateTime.UtcNow), "sys", "sys");
        
        var history = job.StatusHistory.First();
        
        dbContext.DeliveryJobs.Add(job);
        await dbContext.SaveChangesAsync();
        
        // Act
        dbContext.Entry(history).State = EntityState.Modified; // Try modifying the history
        
        var action = async () => await dbContext.SaveChangesAsync();
        
        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DeliveryStatusHistory is append-only and cannot be modified or deleted.");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDeletingDeliveryStatusHistory_ThrowsInvalidOperationException()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        
        var job = DeliveryJob.CreatePending(
            Guid.NewGuid(), "REF", Guid.NewGuid(), "ORD", "Type",
            Guid.NewGuid(), "Shop", "Owner", "test@test.com",
            Guid.NewGuid(), "Agency", "test@test.com",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "LKR", new List<DeliveryJobLine>());
            
        job.Assign(Guid.NewGuid(), "Rep", DateOnly.FromDateTime(DateTime.UtcNow), "sys", "sys");
        var history = job.StatusHistory.First();
        
        dbContext.DeliveryJobs.Add(job);
        await dbContext.SaveChangesAsync();
        
        // Act
        dbContext.DeliveryStatusHistories.Remove(history); // Try deleting the history
        
        var action = async () => await dbContext.SaveChangesAsync();
        
        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DeliveryStatusHistory is append-only and cannot be modified or deleted.");
    }
}
