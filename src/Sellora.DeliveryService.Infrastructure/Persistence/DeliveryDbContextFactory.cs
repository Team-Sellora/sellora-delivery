using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sellora.DeliveryService.Infrastructure.Persistence;

// Used by dotnet-ef migrations tooling — not referenced at runtime.
public class DeliveryDbContextFactory : IDesignTimeDbContextFactory<DeliveryDbContext>
{
    public DeliveryDbContext CreateDbContext(string[] args)
    {
        // Set ConnectionStrings__DeliveryDb in the environment before running dotnet-ef.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DeliveryDb")
            ?? throw new InvalidOperationException(
                "Set the ConnectionStrings__DeliveryDb environment variable to run EF migrations.");

        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(DeliveryDbContext).Assembly.FullName))
            .Options;

        // Pass null for ITenantContext — the design-time factory has no HTTP context,
        // so no query filters are applied (which is correct for migrations).
        return new DeliveryDbContext(options, tenantContext: null);
    }
}
