using EvolutionaryLogistics.BuildingBlocks.Caching;
using MediatR;

namespace EvolutionaryLogistics.Services.Inventory.Application.Queries;

/// <summary>
/// Example cacheable query: Get inventory availability for a SKU.
///
/// USAGE IN LAB:
/// Participants implement this query, test cache hits in Redis CLI,
/// then trigger InventoryReserved event to observe automatic invalidation.
/// </summary>
public sealed record GetInventoryAvailabilityQuery(string Sku) : IRequest<InventoryAvailabilityResponse>, ICacheableQuery
{
    // ── Cache key convention: {context}:{entity}:{identifier} ──
    public string CacheKey => $"inventory:availability:{Sku}";

    // Short TTL for volatile inventory data
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromSeconds(30);
    public TimeSpan? SlidingExpiration => null;
}

public sealed record InventoryAvailabilityResponse(
    string Sku,
    int AvailableQuantity,
    int ReservedQuantity,
    DateTimeOffset LastUpdatedAt);
