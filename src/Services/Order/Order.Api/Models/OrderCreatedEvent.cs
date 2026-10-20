
namespace Order.Models;

public sealed record OrderCreatedEvent(
    Guid OrderId,
    string ProductCode,
    int Quantity,
    DateTime CreatedAtUtc
);
