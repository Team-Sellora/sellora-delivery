using Confluent.Kafka;

namespace Sellora.DeliveryService.Infrastructure.Kafka;

internal static class KafkaSaslConfigurator
{
    public static void Apply(ClientConfig config, KafkaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SecurityProtocol)) return;

        config.SecurityProtocol = Enum.Parse<SecurityProtocol>(options.SecurityProtocol, ignoreCase: true);

        if (!string.IsNullOrWhiteSpace(options.SaslMechanism))
            config.SaslMechanism = Enum.Parse<SaslMechanism>(options.SaslMechanism, ignoreCase: true);

        config.SaslUsername = options.SaslUsername;
        config.SaslPassword = options.SaslPassword;
    }
}
