using Confluent.Kafka;
using FluentAssertions;
using Sellora.DeliveryService.Infrastructure.Kafka;
using Xunit;

namespace Sellora.DeliveryService.Tests.Unit;

public class KafkaSaslConfiguratorTests
{
    [Fact]
    public void Apply_NoSecurityProtocol_LeavesConfigUntouched()
    {
        var config = new ClientConfig();

        KafkaSaslConfigurator.Apply(config, new KafkaOptions());

        config.SecurityProtocol.Should().BeNull();
        config.SaslUsername.Should().BeNull();
    }

    [Fact]
    public void Apply_WithSaslSettings_CopiesAllValues()
    {
        var config = new ClientConfig();
        var options = new KafkaOptions
        {
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "plain",
            SaslUsername = "user",
            SaslPassword = "value"
        };

        KafkaSaslConfigurator.Apply(config, options);

        config.SecurityProtocol.Should().Be(SecurityProtocol.SaslSsl);
        config.SaslMechanism.Should().Be(SaslMechanism.Plain);
        config.SaslUsername.Should().Be("user");
        config.SaslPassword.Should().Be("value");
    }

    [Fact]
    public void Apply_WithoutMechanism_SkipsMechanism()
    {
        var config = new ClientConfig();

        KafkaSaslConfigurator.Apply(config, new KafkaOptions { SecurityProtocol = "ssl" });

        config.SecurityProtocol.Should().Be(SecurityProtocol.Ssl);
        config.SaslMechanism.Should().BeNull();
    }
}
