using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EvolutionaryLogistics.BuildingBlocks.Caching;

/// <summary>
/// Marker interface for domain events that trigger cache invalidation.
/// </summary>
public interface ICacheInvalidatingEvent : INotification
{
    /// <summary>
    /// Cache keys to invalidate when this event fires.
    /// Supports patterns with Redis key-scan for bulk invalidation.
    /// </summary>
    IReadOnlyCollection<string> InvalidationKeys { get; }

    /// <summary>
    /// Optional: Redis Pub/Sub channel for cross-instance invalidation.
    /// When set, a message is published so other instances of the same service
    /// can evict their local L1 cache as well.
    /// </summary>
    string? PubSubChannel => null;
}

/// <summary>
/// MediatR Notification Handler: Domain Event → Cache Invalidation.
///
/// FLOW:
/// ─────
///   Domain Event (e.g., InventoryReserved)
///     → CacheInvalidationHandler
///       → Remove exact keys from Redis
///       → Publish to Redis Pub/Sub channel (cross-instance invalidation)
///       → Other instances receive Pub/Sub → evict local cache
///
/// DESIGN DECISIONS:
/// ─────────────────
/// 1) Notification (not Behavior): Invalidation happens AFTER the command handler
///    succeeds. If we used a Behavior on the write path, a failed invalidation
///    would block the write — wrong trade-off for most systems.
///
/// 2) Fire-and-forget invalidation: We don't await Pub/Sub confirmation.
///    Redis Pub/Sub is at-most-once. If a subscriber misses the message,
///    the TTL-based expiration provides a safety net.
///    GOTCHA: This means there's a window where stale data can be served.
///    For most CQRS systems, this is acceptable (eventual consistency).
///
/// 3) Pattern-based invalidation: "inventory:SKU-*" deletes all cached
///    views for a SKU family. Uses SCAN (not KEYS) to avoid blocking Redis.
///    GOTCHA: SCAN is O(N) where N = total keys in Redis. For millions of keys,
///    consider structured key namespaces instead of wildcard patterns.
///
/// 4) L1/L2 cache: This handler invalidates L2 (Redis). L1 (in-memory) is
///    invalidated via the Pub/Sub listener registered at startup.
/// </summary>
public sealed class CacheInvalidationHandler<TEvent>(
    IDistributedCache cache,
    IConnectionMultiplexer redis,
    ILogger<CacheInvalidationHandler<TEvent>> logger) : INotificationHandler<TEvent>
    where TEvent : ICacheInvalidatingEvent
{
    private static readonly ActivitySource ActivitySource = new("BuildingBlocks.CacheInvalidation");

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("CacheInvalidation");
        activity?.SetTag("event.type", typeof(TEvent).Name);
        activity?.SetTag("keys.count", notification.InvalidationKeys.Count);

        var db = redis.GetDatabase();
        var invalidated = 0;

        foreach (var key in notification.InvalidationKeys)
        {
            try
            {
                if (key.Contains('*'))
                {
                    // ── Pattern-based invalidation using SCAN ──
                    // NEVER use KEYS command in production — it blocks Redis.
                    // SCAN is cursor-based, non-blocking, O(1) per iteration.
                    var server = redis.GetServers().First();
                    await foreach (var redisKey in server.KeysAsync(pattern: key))
                    {
                        await db.KeyDeleteAsync(redisKey);
                        invalidated++;
                    }
                }
                else
                {
                    // Exact key deletion
                    await cache.RemoveAsync(key, cancellationToken);
                    invalidated++;
                }
            }
            catch (Exception ex)
            {
                // ── Non-fatal: cache invalidation failure ──
                // The TTL will eventually expire the stale entry.
                // Alert on this metric for monitoring.
                logger.LogWarning(ex, "Cache invalidation failed for key: {Key}", key);
            }
        }

        // ── Cross-instance invalidation via Pub/Sub ──
        if (notification.PubSubChannel is { } channel)
        {
            try
            {
                var message = JsonSerializer.Serialize(new CacheInvalidationMessage
                {
                    Keys = notification.InvalidationKeys,
                    SourceInstance = Environment.MachineName,
                    Timestamp = DateTimeOffset.UtcNow
                });

                var subscriber = redis.GetSubscriber();
                await subscriber.PublishAsync(
                    RedisChannel.Literal(channel),
                    message);

                logger.LogDebug("Published cache invalidation to {Channel}: {Count} keys",
                    channel, notification.InvalidationKeys.Count);
            }
            catch (Exception ex)
            {
                // Pub/Sub failure is non-fatal — other instances will rely on TTL
                logger.LogWarning(ex, "Pub/Sub invalidation publish failed on {Channel}", channel);
            }
        }

        activity?.SetTag("keys.invalidated", invalidated);
        logger.LogDebug("Cache invalidation completed: {Invalidated} keys for {Event}",
            invalidated, typeof(TEvent).Name);
    }
}

/// <summary>
/// Message published via Redis Pub/Sub for cross-instance cache invalidation.
/// </summary>
public sealed record CacheInvalidationMessage
{
    public required IReadOnlyCollection<string> Keys { get; init; }
    public required string SourceInstance { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Background service that subscribes to Redis Pub/Sub for cache invalidation
/// from other instances. Evicts matching keys from the local IDistributedCache.
///
/// Register this as a hosted service:
///   builder.Services.AddHostedService&lt;CacheInvalidationSubscriber&gt;();
/// </summary>
public sealed class CacheInvalidationSubscriber(
    IConnectionMultiplexer redis,
    IDistributedCache cache,
    ILogger<CacheInvalidationSubscriber> logger) : IHostedService
{
    private readonly string _instanceId = Environment.MachineName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var subscriber = redis.GetSubscriber();

        // Subscribe to the shared invalidation channel
        await subscriber.SubscribeAsync(
            RedisChannel.Literal("cache:invalidation"),
            async (_, message) =>
            {
                try
                {
                    var msg = JsonSerializer.Deserialize<CacheInvalidationMessage>(message.ToString());
                    if (msg is null || msg.SourceInstance == _instanceId)
                    {
                        // Skip messages from self — we already invalidated locally
                        return;
                    }

                    foreach (var key in msg.Keys)
                    {
                        await cache.RemoveAsync(key);
                    }

                    logger.LogDebug("Received cross-instance invalidation from {Source}: {Count} keys",
                        msg.SourceInstance, msg.Keys.Count);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process cache invalidation message");
                }
            });

        logger.LogInformation("Cache invalidation subscriber started on instance {Instance}", _instanceId);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Cache invalidation subscriber stopping");
        return Task.CompletedTask;
    }
}
