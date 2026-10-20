
using System.Text.Json;
using Confluent.Kafka;
using Inventory.Api.Models;
using Inventory.Api.Services;


namespace Inventory.Consumers;

public sealed class OrderCreatedConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly InventoryProcessor _processor;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        IConfiguration configuration,
        InventoryProcessor processor,
        ILogger<OrderCreatedConsumer> logger)
    {
        _configuration = configuration;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers =
                _configuration["Kafka:BootstrapServers"]
                ?? "localhost:9092",

            GroupId =
                _configuration["Kafka:GroupId"]
                ?? "inventory-service-group",

            AutoOffsetReset = AutoOffsetReset.Earliest,

            EnableAutoCommit = false
        };

        var topic =
            _configuration["Kafka:Topic"] ?? "orders.created";

        using var consumer = new ConsumerBuilder<string, string>(
            config).Build();

        consumer.Subscribe(topic);

        _logger.LogInformation(
            "Inventory Kafka dinleyicisi başladı. Topic: {Topic}",
            topic);
        Console.WriteLine($"Inventory Kafka dinleyicisi başladı. Topic: {topic}");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;

                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(
                        ex, "Kafka mesajı alınırken hata oluştu.");

                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                if (result?.Message?.Value is not { } json)
                    continue;

                try
                {
                    var order =
                        JsonSerializer.Deserialize<OrderCreatedEvent>(
                            json);

                    if (order is null)
                    {
                        _logger.LogError(
                            "Mesaj çözümlenemedi. Partition: {Partition}, Offset: {Offset}",
                            result.Partition.Value,
                            result.Offset.Value);

                        // Hatalı mesajı tekrar tekrar okumamak için
                        // bu örnekte offset ilerletiliyor.
                        consumer.Commit(result);
                        continue;
                    }

                    _logger.LogInformation(
                        "Mesaj alındı. Partition: {Partition}, Offset: {Offset}",
                        result.Partition.Value,
                        result.Offset.Value);

                    await _processor.ProcessAsync(
                        order, stoppingToken);

                    // İşleme tamamlandıktan sonra offset'i commit et.
                    consumer.Commit(result);

                    _logger.LogInformation(
                        "Kafka mesajı işlendi ve offset commit edildi. OrderId: {OrderId}",
                        order.OrderId);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Mesaj işlenemedi. Partition: {Partition}, Offset: {Offset}",
                        result.Partition.Value,
                        result.Offset.Value);

                    // Başarısız mesajı commit etmiyoruz.
                    // Bu örnekte consumer yeniden başlarsa mesaj
                    // tekrar işlenebilir.
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }
}
