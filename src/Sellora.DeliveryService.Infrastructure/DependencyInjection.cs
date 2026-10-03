using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Infrastructure.Kafka;
using Sellora.DeliveryService.Infrastructure.Outbox;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Sellora.DeliveryService.Infrastructure.Services;
using Sellora.DeliveryService.Infrastructure.Tenancy;

namespace Sellora.DeliveryService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));

        services.AddDbContext<DeliveryDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration.GetConnectionString("DeliveryDb"),
                npgsql => npgsql.MigrationsAssembly(typeof(DeliveryDbContext).Assembly.FullName)));

        services.AddScoped<IDeliveryJobRepository, DeliveryJobRepository>();
        services.AddScoped<IOutboxWriter, EntityFrameworkOutboxWriter>();
        services.AddScoped<IDeliveryReferenceGenerator, DeliveryReferenceGenerator>();
        services.AddScoped<IDeliveryJobService, DeliveryJobService>();

        services.AddHostedService<OrderEventConsumerService>();
        services.AddHostedService<OutboxRelayService>();

        return services;
    }
}
