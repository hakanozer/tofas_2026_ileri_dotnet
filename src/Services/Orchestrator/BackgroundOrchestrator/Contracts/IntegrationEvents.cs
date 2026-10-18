namespace EvolutionaryLogistics.Services.Orchestrator.Contracts;

/// <summary>
/// Integration events published via the Outbox.
/// These contracts are shared between Order, Inventory, and Orchestrator services.
/// In production, extract to a shared NuGet package (Order.Contracts).
/// </summary>

public sealed record OrderPlacedEvent
{
    public required Guid OrderId { get; init; }
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public sealed record InventoryReservedEvent
{
    public required Guid OrderId { get; init; }
    public required string Sku { get; init; }
    public required int ReservedQuantity { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public sealed record InventoryReservationFailedEvent
{
    public required Guid OrderId { get; init; }
    public required string Sku { get; init; }
    public required string Reason { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public sealed record OrderConfirmedEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

public sealed record OrderCancelledEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}
