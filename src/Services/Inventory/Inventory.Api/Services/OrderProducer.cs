
using System.Text.Json;
using Confluent.Kafka;
using Inventory.Api.Models;

namespace Order.Services;

public sealed class OrderProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public OrderProducer(IConfiguration configuration)
    {
        var bootstrapServers =
            configuration["Kafka:BootstrapServers"]
            ?? "localhost:9092";

        _topic = configuration["Kafka:Topic"]
            ?? "orders.created";

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config)
            .Build();
    }

    public async Task SendAsync(
        OrderCreatedEvent order,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(order);

        var message = new Message<string, string>
        {
            Key = order.OrderId.ToString(),
            Value = json
        };

        await _producer.ProduceAsync(
            _topic,
            message,
            cancellationToken);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
