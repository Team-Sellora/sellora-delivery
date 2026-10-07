using Microsoft.EntityFrameworkCore;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Domain.Entities;

namespace Sellora.DeliveryService.Infrastructure.Persistence;

public class DeliveryDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public DeliveryDbContext(DbContextOptions<DeliveryDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<DeliveryJob> DeliveryJobs => Set<DeliveryJob>();
    public DbSet<DeliveryJobLine> DeliveryJobLines => Set<DeliveryJobLine>();
    public DbSet<DeliveryStatusHistory> DeliveryStatusHistories => Set<DeliveryStatusHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedDeliveryEvent> ProcessedDeliveryEvents => Set<ProcessedDeliveryEvent>();

    public Guid TenantId => _tenantContext?.CompanyId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveryDbContext).Assembly);

        // Global tenant filter evaluates TenantId dynamically per-query.
        // If TenantId is Guid.Empty (e.g. at startup migrations), it will filter by Guid.Empty,
        // but no queries run at startup anyway.
        modelBuilder.Entity<DeliveryJob>().HasQueryFilter(j => j.CompanyId == TenantId);
        modelBuilder.Entity<DeliveryJobLine>().HasQueryFilter(l => l.CompanyId == TenantId);
        modelBuilder.Entity<DeliveryStatusHistory>().HasQueryFilter(h => h.CompanyId == TenantId);
        modelBuilder.Entity<OutboxMessage>().HasQueryFilter(o => o.CompanyId == TenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAppendOnlyHistory()
    {
        foreach (var entry in ChangeTracker.Entries<DeliveryStatusHistory>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("DeliveryStatusHistory is append-only and cannot be modified or deleted.");
            }
        }
    }
}
