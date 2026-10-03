using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Sellora.DeliveryService.Domain.Entities;
using Sellora.DeliveryService.Infrastructure.Kafka;

namespace Sellora.DeliveryService.Infrastructure.Outbox;

internal static class KafkaEventPublisher
{
    // Standard headers used across all Sellora services
    private const string HeaderCorrelationId = "x-correlation-id";
    private const string HeaderEventType = "x-event-type";
    private const string HeaderCompanyId = "x-company-id";
    private const string HeaderServiceSource = "x-service-source";
    private const string ServiceName = "sellora-delivery";

    public static IProducer<string, string> BuildProducer(KafkaOptions options)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 5
        };

        KafkaSaslConfigurator.Apply(config, options);

        return new ProducerBuilder<string, string>(config).Build();
    }

    public static async Task PublishAsync(
        IProducer<string, string> producer,
        OutboxMessage message,
        ILogger logger,
        CancellationToken ct)
    {
        var kafkaMessage = new Message<string, string>
        {
            Key = message.MessageKey,
            Value = message.Payload,
            Headers = new Headers
            {
                { HeaderEventType, System.Text.Encoding.UTF8.GetBytes(message.EventType) },
                { HeaderCompanyId, System.Text.Encoding.UTF8.GetBytes(message.CompanyId.ToString()) },
                { HeaderCorrelationId, System.Text.Encoding.UTF8.GetBytes(message.OutboxMessageId.ToString()) },
                { HeaderServiceSource, System.Text.Encoding.UTF8.GetBytes(ServiceName) }
            }
        };

        try
        {
            var result = await producer.ProduceAsync(message.Topic, kafkaMessage, ct);
            logger.LogDebug("OutboxRelay published {EventType} to {Topic}/{Partition}@{Offset}",
                message.EventType, result.Topic, result.Partition, result.Offset);
        }
        catch (ProduceException<string, string> ex)
        {
            logger.LogError(ex, "OutboxRelay failed to publish {EventType} to {Topic}",
                message.EventType, message.Topic);
            throw;
        }
    }
}
