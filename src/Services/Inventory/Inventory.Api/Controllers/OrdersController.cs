
using Inventory.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Order.Services;


namespace Order.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderProducer _producer;

    public OrdersController(OrderProducer producer)
    {
        _producer = producer;
    }

    [HttpPost("/push")]
    public async Task<IActionResult> Create(
         CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode)
            || request.Quantity <= 0)
        {
            return BadRequest(new
            {
                message = "ProductCode zorunludur ve Quantity sıfırdan büyük olmalıdır."
            });
        }

        var order = new OrderCreatedEvent(
            Guid.NewGuid(),
            request.ProductCode,
            request.Quantity,
            DateTime.UtcNow
        );

        try
        {
            await _producer.SendAsync(order, cancellationToken);

            return Accepted(new
            {
                message = "Sipariş Kafka kuyruğuna gönderildi.",
                order
            });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return StatusCode(503, new
            {
                message = "Sipariş Kafka'ya gönderilemedi."
            });
        }
    }
}

public sealed record CreateOrderRequest(
    string ProductCode,
    int Quantity
);
