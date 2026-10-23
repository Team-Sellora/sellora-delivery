using Confluent.Kafka;
using Confluent.Kafka.Admin;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.DeliveryService.Application.Common.Interfaces;
using Sellora.DeliveryService.Application.Services;
using Sellora.DeliveryService.Infrastructure.Kafka;
using Sellora.DeliveryService.Infrastructure.Outbox;
using Sellora.DeliveryService.Infrastructure.Persistence;
using Sellora.DeliveryService.Infrastructure.Repositories;
using Sellora.DeliveryService.Infrastructure.Services;
using Sellora.DeliveryService.Infrastructure.Tenancy;

namespace Sellora.DeliveryService.Tests.Integration;

public class OrderKafkaConsumerTests
{
    [Fact]
    public async Task ConfirmedOrder_ConsumedFromKafka_CreatesOneJobAndOutboxEvent()
    {
        // CI supplies a disposable local Kafka broker; no Confluent credentials are used.
        var bootstrap = Environment.GetEnvironmentVariable("DELIVERY_TEST_KAFKA_BOOTSTRAP");
        Assert.False(string.IsNullOrWhiteSpace(bootstrap), "Set DELIVERY_TEST_KAFKA_BOOTSTRAP to run this integration test.");

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification { Name = "sellora.order.v1", NumPartitions = 1, ReplicationFactor = 1 },
            new TopicSpecification { Name = "sellora.delivery.dead-letter.v1", NumPartitions = 1, ReplicationFactor = 1 },
            new TopicSpecification { Name = "sellora.delivery.v1", NumPartitions = 1, ReplicationFactor = 1 }
        });

        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<SystemTenantContext>();
        services.AddScoped<ISystemTenantContext>(sp => sp.GetRequiredService<SystemTenantContext>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<SystemTenantContext>());
        services.AddDbContext<DeliveryDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IDeliveryJobRepository, DeliveryJobRepository>();
        services.AddScoped<IOutboxWriter, EntityFrameworkOutboxWriter>();
        services.AddScoped<IDeliveryReferenceGenerator, DeliveryReferenceGenerator>();
        services.AddScoped<IDeliveryJobService, DeliveryJobService>();

        await using var provider = services.BuildServiceProvider();
        var consumer = new OrderEventConsumerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions { BootstrapServers = bootstrap! }),
            NullLogger<OrderEventConsumerService>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var eventId = Guid.NewGuid();
            using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build();
            await producer.ProduceAsync("sellora.order.v1", new Message<string, string>
            {
                Key = "ORD-260924-K7P2QM",
                Value = OrderContractTests.ConfirmedJson(eventId)
            });

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
                if (await db.DeliveryJobs.IgnoreQueryFilters().AnyAsync(j => j.OrderId == OrderContractTests.OrderId))
                {
                    Assert.Single(await db.DeliveryJobs.IgnoreQueryFilters().ToListAsync());
                    Assert.Single(await db.OutboxMessages.IgnoreQueryFilters().ToListAsync());
                    Assert.Single(await db.ProcessedDeliveryEvents.IgnoreQueryFilters().ToListAsync());
                    var relay = new OutboxRelayService(
                        provider.GetRequiredService<IServiceScopeFactory>(),
                        Options.Create(new KafkaOptions { BootstrapServers = bootstrap! }),
                        NullLogger<OutboxRelayService>.Instance);
                    using var deliveryConsumer = new ConsumerBuilder<string, string>(new ConsumerConfig
                    {
                        BootstrapServers = bootstrap,
                        GroupId = $"delivery-test-{Guid.NewGuid():N}",
                        AutoOffsetReset = AutoOffsetReset.Earliest
                    }).Build();
                    deliveryConsumer.Subscribe("sellora.delivery.v1");
                    await relay.StartAsync(CancellationToken.None);
                    try
                    {
                        var published = deliveryConsumer.Consume(TimeSpan.FromSeconds(20));
                        Assert.NotNull(published);
                        Assert.Equal("ORD-260924-K7P2QM", published.Message.Key);
                        Assert.Contains("DeliveryJobCreated", published.Message.Value);
                    }
                    finally
                    {
                        await relay.StopAsync(CancellationToken.None);
                        relay.Dispose();
                        deliveryConsumer.Close();
                    }

                    // A malformed order event must be observable on the dead-letter topic,
                    // rather than silently disappearing after the offset is committed.
                    using var deadLetterConsumer = new ConsumerBuilder<string, string>(new ConsumerConfig
                    {
                        BootstrapServers = bootstrap,
                        GroupId = $"delivery-dlq-test-{Guid.NewGuid():N}",
                        AutoOffsetReset = AutoOffsetReset.Earliest
                    }).Build();
                    deadLetterConsumer.Subscribe("sellora.delivery.dead-letter.v1");
                    await producer.ProduceAsync("sellora.order.v1", new Message<string, string>
                    {
                        Key = "invalid-order",
                        Value = "{invalid json"
                    });

                    var deadLetter = deadLetterConsumer.Consume(TimeSpan.FromSeconds(20));
                    Assert.NotNull(deadLetter);
                    using var payload = JsonDocument.Parse(deadLetter.Message.Value);
                    Assert.Equal("sellora.order.v1", payload.RootElement.GetProperty("SourceTopic").GetString());
                    Assert.Equal("{invalid json", payload.RootElement.GetProperty("OriginalValue").GetString());
                    Assert.StartsWith("DeserialiseFailure:", payload.RootElement.GetProperty("Reason").GetString());
                    deadLetterConsumer.Close();
                    return;
                }
                await Task.Delay(500);
            }

            Assert.Fail("OrderConfirmed was not processed from Kafka within 30 seconds.");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
            consumer.Dispose();
        }
    }
}
