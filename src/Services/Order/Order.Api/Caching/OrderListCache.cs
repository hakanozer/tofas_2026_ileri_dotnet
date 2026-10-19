using System.Text.Json;
using Microsoft.Extensions.Options;
using Order.Api.Data;
using StackExchange.Redis;

namespace Order.Api.Caching;

public sealed class OrderCacheOptions
{
    public const string Section = "Cache";
    public int OrderListTtlSeconds { get; set; } = 60;
}

public sealed record OrderItemDto(string Sku, int Quantity);

public sealed record OrderDto(
    Guid Id,
    string Status,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto From(CustomerOrder o) => new(
        o.Id, o.Status.ToString(), o.RejectionReason, o.CreatedAt, o.UpdatedAt,
        o.Items.Select(i => new OrderItemDto(i.Sku, i.Quantity)).ToList());
}

/// <summary>Kullanıcı bazlı sipariş listesi cache'i. Anahtar daima userId içerir; kullanıcılar arasında paylaşım yoktur.</summary>
public interface IOrderListCache
{
    Task<IReadOnlyList<OrderDto>?> GetAsync(Guid userId, CancellationToken ct);
    Task SetAsync(Guid userId, IReadOnlyList<OrderDto> orders, CancellationToken ct);
    Task InvalidateAsync(Guid userId, CancellationToken ct);
}

public sealed class RedisOrderListCache(
    IConnectionMultiplexer redis,
    IOptions<OrderCacheOptions> options,
    ILogger<RedisOrderListCache> logger) : IOrderListCache
{
    public static string Key(Guid userId) => $"orders:user:{userId}:list";

    public async Task<IReadOnlyList<OrderDto>?> GetAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(userId));
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<List<OrderDto>>((string)value!);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            // Cache arızası isteği düşürmemeli: DB'ye düşülür.
            logger.LogWarning(ex, "Order list cache read failed for user {UserId}", userId);
            return null;
        }
    }

    public async Task SetAsync(Guid userId, IReadOnlyList<OrderDto> orders, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(
                Key(userId), JsonSerializer.Serialize(orders), TimeSpan.FromSeconds(options.Value.OrderListTtlSeconds));
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Order list cache write failed for user {UserId}", userId);
        }
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(userId));
        }
        catch (RedisException ex)
        {
            // Bayat veri en fazla TTL kadar kalır.
            logger.LogWarning(ex, "Order list cache invalidation failed for user {UserId}", userId);
        }
    }
}
