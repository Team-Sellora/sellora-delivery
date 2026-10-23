using System.Text;
using System.Text.Json;
using Confluent.Kafka;

var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
using var producer = new ProducerBuilder<string, string>(config).Build();

var companyId = Guid.NewGuid();
var orderId = Guid.NewGuid();
var eventId = Guid.NewGuid();

var payload = new
{
    eventId = eventId,
    eventType = "OrderConfirmed",
    companyId = companyId,
    orderId = orderId,
    orderReference = $"ORD-TEST-{new Random().Next(1000, 9999)}",
    occurredAt = DateTimeOffset.UtcNow,
    fulfilmentType = "ImmediateCashSale", // Change to "ScheduledDelivery" to test the other flow
    shopId = Guid.NewGuid(),
    shopName = "Test Shop",
    shopOwnerName = "John Doe",
    shopOwnerEmail = "test@example.com",
    agencyId = Guid.NewGuid(),
    agencyName = "Test Agency",
    agencyEmail = "agency@example.com",
    territoryId = Guid.NewGuid(),
    provinceId = Guid.NewGuid(),
    orderSalesRepId = Guid.NewGuid(),
    total = 2500.00m,
    currency = "MWK",
    lines = new[]
    {
        new { productId = Guid.NewGuid(), productName = "Product A", quantity = 10, unitPrice = 100m, lineTotal = 1000m },
        new { productId = Guid.NewGuid(), productName = "Product B", quantity = 5, unitPrice = 300m, lineTotal = 1500m }
    },
    checkoutLocation = new { locationId = Guid.NewGuid(), locationName = "Van 1", checkedInAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
    confirmedAt = DateTimeOffset.UtcNow
};

var message = new Message<string, string>
{
    Key = orderId.ToString(),
    Value = JsonSerializer.Serialize(payload),
    Headers = new Headers
    {
        { "x-company-id", Encoding.UTF8.GetBytes(companyId.ToString()) },
        { "x-event-type", Encoding.UTF8.GetBytes("OrderConfirmed") },
        { "x-correlation-id", Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()) }
    }
};

Console.WriteLine($"Publishing OrderConfirmed event to sellora.order.v1...");
var result = await producer.ProduceAsync("sellora.order.v1", message);
Console.WriteLine($"Delivered to: {result.TopicPartitionOffset}");
