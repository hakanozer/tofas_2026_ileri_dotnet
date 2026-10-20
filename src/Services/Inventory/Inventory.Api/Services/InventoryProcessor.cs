
using Inventory.Api.Models;
namespace Inventory.Api.Services;

public sealed class InventoryProcessor
{
    private readonly ILogger<InventoryProcessor> _logger;

    public InventoryProcessor(
        ILogger<InventoryProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ProcessAsync(
        OrderCreatedEvent order,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Sipariş alındı. OrderId: {OrderId}. 1000 ms bekleniyor...",
            order.OrderId);

        await Task.Delay(
            TimeSpan.FromMilliseconds(1000),
            cancellationToken);

        // Veritabanı yerine işlemi konsola yazıyoruz.
        _logger.LogInformation(
            "ENVANTER İŞLENDİ | OrderId: {OrderId} | ProductCode: {ProductCode} | Quantity: {Quantity}",
            order.OrderId,
            order.ProductCode,
            order.Quantity);
    }
}
