using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Kafka;
using Sellora.DeliveryService.Infrastructure.Persistence;

namespace Sellora.DeliveryService.Infrastructure.Outbox;

internal class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    ILogger<OutboxRelayService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IProducer<string, string>? producer = null;
        try
        {
            producer = KafkaEventPublisher.BuildProducer(kafkaOptions.Value);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RelayBatchAsync(producer, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "OutboxRelay batch failed; will retry");
                }

                await Task.Delay(PollingInterval, stoppingToken);
            }
        }
        finally
        {
            producer?.Flush(TimeSpan.FromSeconds(5));
            producer?.Dispose();
        }
    }

    private async Task RelayBatchAsync(IProducer<string, string> producer, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        var leaseId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        // Acquire leases atomically — skip any row already leased by another relay instance
        var batch = await db.OutboxMessages
            .IgnoreQueryFilters()
            .Where(m => m.ProcessedAt == null && (m.LeaseExpiresAt == null || m.LeaseExpiresAt < now))
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var msg in batch)
            msg.AcquireLease(leaseId, LeaseDuration);

        await db.SaveChangesAsync(ct);

        // Publish in per-key order so downstream consumers see a consistent sequence per order
        var byKey = batch.GroupBy(m => m.MessageKey);
        foreach (var group in byKey)
        {
            foreach (var msg in group.OrderBy(m => m.CreatedAt))
            {
                await KafkaEventPublisher.PublishAsync(producer, msg, logger, ct);
            }
        }

        foreach (var msg in batch)
            msg.MarkProcessed();

        await db.SaveChangesAsync(ct);
    }
}
