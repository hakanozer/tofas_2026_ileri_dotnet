using System.Diagnostics;
using System.Text.Json;
using Medallion.Threading;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EvolutionaryLogistics.BuildingBlocks.Caching;

/// <summary>
/// Marker interface for queries that should be cached.
/// Implement on your IRequest to enable automatic cache-aside.
/// </summary>
public interface ICacheableQuery
{
    /// <summary>
    /// Unique cache key for this query.
    /// Convention: "{EntityType}:{identifier}:{variant}"
    /// Example: "inventory:SKU-1234:availability"
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// Cache entry lifetime. Keep short for volatile data.
    /// GOTCHA: Setting this too high with eventual consistency
    /// means users see stale data for longer after writes.
    /// </summary>
    TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Sliding expiration — resets on each cache hit.
    /// Useful for frequently-accessed, rarely-changing data.
    /// Set to null for strict TTL control.
    /// </summary>
    TimeSpan? SlidingExpiration => null;

    /// <summary>
    /// Single-flight kilidi için maksimum bekleme.
    /// Cache MISS olduğunda yalnızca <b>bir</b> istek DB'ye gider; diğerleri kilit
    /// alınamazsa kısa bekleyip cache'i tekrar okur (stampede savunması — Gün 3 Lab 9).
    /// </summary>
    TimeSpan LockTimeout => TimeSpan.FromSeconds(5);
}

/// <summary>
/// MediatR Pipeline Behavior: Cache-Aside pattern for read queries.
///
/// FLOW:
/// ─────
///   Request → [Check Redis] → HIT → return cached
///                            → MISS → single-flight lock → execute handler → serialize → store → return
///
/// DESIGN DECISIONS:
/// ─────────────────
/// 1) IDistributedCache abstraction: allows swapping Redis for NCache, Memcached, etc.
///    But we optimize for Redis-specific features (pub/sub invalidation) in the
///    companion CacheInvalidationHandler.
///
/// 2) Single-flight (stampede protection): On a MISS we take a distributed lock keyed
///    by the cache key. Only the winner hits the database; losers wait briefly and
///    re-read the cache. This turns N concurrent misses into 1 DB query.
///    GOTCHA: The lock provider is REQUIRED. If Redis is down the lock call throws —
///    we catch, log, and fall through to the handler (degraded, but never fail the request).
///
/// 3) JSON serialization: System.Text.Json. GOTCHA: never cache Span-based/ref-struct types.
///
/// 4) OTEL integration: cache hits/misses are tagged on the current Activity for Jaeger/Grafana.
/// </summary>
public sealed class CacheAsideBehavior<TRequest, TResponse>(
    IDistributedCache cache,
    IDistributedLockProvider lockProvider,
    ILogger<CacheAsideBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ActivitySource ActivitySource = new("BuildingBlocks.Caching");

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery cacheable)
        {
            return await next(cancellationToken);
        }

        using var activity = ActivitySource.StartActivity("CacheAside");
        activity?.SetTag("cache.key", cacheable.CacheKey);

        // ── 1) HIZLI YOL: cache HIT ──
        var cachedValue = await TryReadCacheAsync<TResponse>(cacheable.CacheKey, cancellationToken);
        if (cachedValue is not null)
        {
            activity?.SetTag("cache.hit", true);
            logger.LogDebug("Cache HIT: {Key}", cacheable.CacheKey);
            return cachedValue;
        }

        // ── 2) CACHE MISS → single-flight ──
        activity?.SetTag("cache.hit", false);
        logger.LogDebug("Cache MISS: {Key}", cacheable.CacheKey);

        TResponse response;
        IDistributedSynchronizationHandle? handle = null;
        try
        {
            var gate = lockProvider.CreateLock($"lock:{cacheable.CacheKey}");
            handle = await gate.TryAcquireAsync(cacheable.LockTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            // Lock provider'ı Redis'tir; Redis down ise kilit alınamaz.
            // Bu bir business failure DEĞİL — handler'ı çalıştırıp devam ederiz.
            logger.LogWarning(ex, "Distributed lock acquire failed for {Key} — degrading to direct load", cacheable.CacheKey);
        }

        if (handle is null)
        {
            // Kilidi alan başka bir istek var. Kısa bekleyip cache'i tekrar dene;
            // hâlâ yoksa (winner hata aldı vb.) kendimiz yükleriz — TTL güvenlik ağı.
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            var afterWait = await TryReadCacheAsync<TResponse>(cacheable.CacheKey, cancellationToken);
            if (afterWait is not null)
            {
                activity?.SetTag("cache.single_flight", "waited");
                return afterWait;
            }

            activity?.SetTag("cache.single_flight", "fallback");
            return await next(cancellationToken);
        }

        await using (handle)
        {
            // ── Double-checked: kilidi beklerken başkası yazmış olabilir ──
            var afterLock = await TryReadCacheAsync<TResponse>(cacheable.CacheKey, cancellationToken);
            if (afterLock is not null)
            {
                activity?.SetTag("cache.single_flight", "double_checked");
                return afterLock;
            }

            activity?.SetTag("cache.single_flight", "leader");
            response = await next(cancellationToken);

            // ── 3) Cache'e yaz ──
            var options = new DistributedCacheEntryOptions();
            if (cacheable.AbsoluteExpiration is { } absolute)
                options.AbsoluteExpirationRelativeToNow = absolute;
            if (cacheable.SlidingExpiration is { } sliding)
                options.SlidingExpiration = sliding;

            try
            {
                var serialized = JsonSerializer.Serialize(response);
                await cache.SetStringAsync(cacheable.CacheKey, serialized, options, cancellationToken);
            }
            catch (Exception ex)
            {
                // ── GOTCHA: Cache write failure is NON-FATAL ──
                // Never fail a business operation because cache is down.
                // Alert on this metric, but don't throw.
                logger.LogWarning(ex, "Cache SET failed for {Key}", cacheable.CacheKey);
            }
        }

        return response;
    }

    /// <summary>
    /// Cache'ten okur; bozuk (corrupt) JSON'da entry'yi evict eder ve <c>null</c> döner
    /// (çağıran handler'a düşer).
    /// </summary>
    private async Task<TValue?> TryReadCacheAsync<TValue>(string key, CancellationToken cancellationToken)
    {
        var cached = await cache.GetStringAsync(key, cancellationToken);
        if (cached is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<TValue>(cached);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Cache deserialization failed for {Key}, evicting", key);
            await cache.RemoveAsync(key, cancellationToken);
            return default;
        }
    }
}
