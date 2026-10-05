using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Consumers.Events;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Persistence;

namespace Sellora.DeliveryService.Infrastructure.Kafka;

internal class OrderEventConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    ILogger<OrderEventConsumerService> logger)
    : BackgroundService
{
    private const string OrderTopic = "sellora.order.v1";
    private const string ConsumerGroup = "sellora.delivery.order.v1";
    private const string OrderConfirmedEventType = "OrderConfirmed";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            GroupId = ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        KafkaSaslConfigurator.Apply(config, kafkaOptions.Value);

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(OrderTopic);

        logger.LogInformation("OrderEventConsumer subscribed to {Topic} as {GroupId}", OrderTopic, ConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    await HandleMessageAsync(result, stoppingToken);

                    // Offset committed only after the DB transaction commits — guarantees
                    // at-least-once delivery without creating duplicate jobs (idempotency receipt handles that).
                    consumer.Commit(result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "OrderEventConsumer consume error at {Topic}", OrderTopic);
                }
                catch (Exception ex) when (result != null)
                {
                    logger.LogError(ex, "OrderEventConsumer unhandled error processing {Topic}/{Partition}@{Offset}",
                        result.Topic, result.Partition, result.Offset);
                    await SendToDeadLetterAsync(result, ex.Message, stoppingToken);
                    consumer.Commit(result);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleMessageAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        OrderEventBase? baseEvent;
        try
        {
            baseEvent = JsonSerializer.Deserialize<OrderEventBase>(result.Message.Value, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OrderEventConsumer failed to deserialise message from {Topic}/{Partition}@{Offset}",
                result.Topic, result.Partition, result.Offset);
            await SendToDeadLetterAsync(result, "DeserialiseFailure: " + ex.Message, ct);
            return;
        }

        if (baseEvent is null)
        {
            await SendToDeadLetterAsync(result, "DeserialiseFailure: null result", ct);
            return;
        }

        // Only OrderConfirmed triggers job creation; other event types are silently skipped.
        if (baseEvent.EventType != OrderConfirmedEventType)
        {
            logger.LogDebug("OrderEventConsumer skipping event type {EventType}", baseEvent.EventType);
            return;
        }

        OrderConfirmedEvent confirmed;
        try
        {
            confirmed = JsonSerializer.Deserialize<OrderConfirmedEvent>(result.Message.Value, JsonOpts)!;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OrderEventConsumer failed to deserialise OrderConfirmedEvent {OrderReference}",
                baseEvent.OrderReference);
            await SendToDeadLetterAsync(result, "DeserialiseFailure: " + ex.Message, ct);
            return;
        }

        logger.LogInformation("OrderEventConsumer processing {EventType} {OrderReference} company={CompanyId}",
            confirmed.EventType, confirmed.OrderReference, confirmed.CompanyId);

        using var scope = scopeFactory.CreateScope();

        // Set the system tenant scope so the DbContext can be scoped to the correct company
        // without a real HTTP request tenant context.
        var systemTenant = scope.ServiceProvider.GetRequiredService<ISystemTenantContext>();
        systemTenant.SetCompanyId(confirmed.CompanyId);

        var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();

        // Idempotency check BEFORE the job write — a second consume of the same event must not
        // create a second job even if the first consumer died after the DB commit but before
        // the Kafka offset commit.
        var alreadyProcessed = await db.ProcessedDeliveryEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.CompanyId == confirmed.CompanyId && e.EventId == confirmed.EventId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("DeliveryEventDuplicate {OrderReference} eventId={EventId} — skipping",
                confirmed.OrderReference, confirmed.EventId);
            return;
        }

        var service = scope.ServiceProvider.GetRequiredService<IDeliveryJobService>();
        await service.CreateFromOrderConfirmedAsync(confirmed, ct);

        db.ProcessedDeliveryEvents.Add(new ProcessedDeliveryEvent(confirmed.CompanyId, confirmed.EventId));

        await db.SaveChangesAsync(ct);
    }

    private async Task SendToDeadLetterAsync(
        ConsumeResult<string, string> source,
        string reason,
        CancellationToken ct)
    {
        var options = kafkaOptions.Value;
        var producerConfig = new ProducerConfig { BootstrapServers = options.BootstrapServers };
        KafkaSaslConfigurator.Apply(producerConfig, options);

        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

        var payload = JsonSerializer.Serialize(new
        {
            SourceTopic = source.Topic,
            Partition = source.Partition.Value,
            Offset = source.Offset.Value,
            OriginalKey = source.Message.Key,
            OriginalValue = source.Message.Value,
            Reason = reason,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await producer.ProduceAsync(
            options.DeadLetterTopic,
            new Message<string, string> { Key = source.Message.Key, Value = payload },
            ct);

        logger.LogWarning("OrderEventConsumer routed unparseable message to dead-letter topic {DeadLetterTopic} source={SourceTopic}/{Partition}@{Offset}",
            options.DeadLetterTopic, source.Topic, source.Partition.Value, source.Offset.Value);
    }
}
