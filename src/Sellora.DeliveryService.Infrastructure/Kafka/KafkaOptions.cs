namespace Sellora.DeliveryService.Infrastructure.Kafka;

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string? SecurityProtocol { get; set; }
    public string? SaslMechanism { get; set; }
    public string DeadLetterTopic { get; set; } = "sellora.delivery.dead-letter.v1";
}
