using StackExchange.Redis;

namespace EvolutionaryLogistics.Gateway.Auth;

/// <summary>
/// Login'de üretilen JWT oturumlarının Redis'teki kaydı. Anahtar JWT'nin "jti" değeridir ve TTL'i token ömrüne eşittir:
/// anahtar yoksa (süre doldu / logout) imzası geçerli olsa bile token reddedilir.
/// </summary>
public interface ITokenSessionStore
{
    Task StoreAsync(string jti, Guid userId, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> IsActiveAsync(string jti, CancellationToken ct = default);
    Task RevokeAsync(string jti, CancellationToken ct = default);
}

public sealed class RedisTokenSessionStore(IConnectionMultiplexer redis) : ITokenSessionStore
{
    public static string Key(string jti) => $"auth:session:{jti}";

    public Task StoreAsync(string jti, Guid userId, TimeSpan ttl, CancellationToken ct = default)
        => redis.GetDatabase().StringSetAsync(Key(jti), userId.ToString(), ttl);

    public Task<bool> IsActiveAsync(string jti, CancellationToken ct = default)
        => redis.GetDatabase().KeyExistsAsync(Key(jti));

    public Task RevokeAsync(string jti, CancellationToken ct = default)
        => redis.GetDatabase().KeyDeleteAsync(Key(jti));
}
