using EvolutionaryLogistics.BuildingBlocks.Caching;
using MediatR;

namespace EvolutionaryLogistics.Services.Inventory.Application.Events;

/// <summary>
/// Domain event fired when inventory is reserved for an order.
/// Triggers cache invalidation for the affected SKU's read models.
///
/// LAB SCENARIO:
/// ─────────────
/// 1) GET /inventory/SKU-1234 → cache MISS → DB query → cache SET
/// 2) GET /inventory/SKU-1234 → cache HIT → fast response
/// 3) POST /orders (reserves SKU-1234) → InventoryReservedDomainEvent fires
/// 4) CacheInvalidationHandler removes "inventory:availability:SKU-1234"
/// 5) GET /inventory/SKU-1234 → cache MISS → DB query (fresh data) → cache SET
///
/// GOTCHA: Between step 3 and 5, there's a window where the cache is cold.
/// Under high traffic, N concurrent requests hit the DB simultaneously
/// (cache stampede). Lab exercise: implement IDistributedLock to prevent this.
/// </summary>
public sealed record InventoryReservedDomainEvent(string Sku, int Quantity, Guid OrderId)
    : INotification, ICacheInvalidatingEvent
{
    public IReadOnlyCollection<string> InvalidationKeys =>
    [
        $"inventory:availability:{Sku}",      // Exact key for this SKU
        $"inventory:dashboard:*",              // Pattern: all dashboard views
        $"inventory:report:low-stock"          // Aggregated report cache
    ];

    public string? PubSubChannel => "cache:invalidation";
}

/// <summary>
/// Domain event fired when reserved inventory is released (order cancelled).
/// </summary>
public sealed record InventoryReleasedDomainEvent(string Sku, int Quantity, Guid OrderId)
    : INotification, ICacheInvalidatingEvent
{
    public IReadOnlyCollection<string> InvalidationKeys =>
    [
        $"inventory:availability:{Sku}",
        $"inventory:dashboard:*"
    ];

    public string? PubSubChannel => "cache:invalidation";
}
