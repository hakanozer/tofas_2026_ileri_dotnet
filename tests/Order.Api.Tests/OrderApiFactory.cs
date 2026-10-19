using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Order.Api.Caching;
using Order.Api.Clients;
using Order.Api.Data;

namespace Order.Api.Tests;

public sealed class FakeInventoryClient : IInventoryClient
{
    public InventoryReserveResult Result { get; set; } = new(InventoryOutcome.Reserved, []);
    public List<Guid> Reserved { get; } = [];
    public List<Guid> Released { get; } = [];

    public Task<InventoryReserveResult> ReserveAsync(Guid orderId, IReadOnlyList<OrderItemDto> items, CancellationToken ct)
    {
        Reserved.Add(orderId);
        return Task.FromResult(Result);
    }

    public Task ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        Released.Add(orderId);
        return Task.CompletedTask;
    }
}

/// <summary>Redis yerine bellek içi cache; hit/miss ve invalidation sayaçlarını tutar.</summary>
public sealed class FakeOrderListCache : IOrderListCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<OrderDto>> _store = new();
    public int Hits;
    public int Misses;
    public int Invalidations;

    public bool Contains(Guid userId) => _store.ContainsKey(RedisOrderListCache.Key(userId));
    public IEnumerable<string> Keys => _store.Keys;

    public Task<IReadOnlyList<OrderDto>?> GetAsync(Guid userId, CancellationToken ct)
    {
        if (_store.TryGetValue(RedisOrderListCache.Key(userId), out var value))
        {
            Interlocked.Increment(ref Hits);
            return Task.FromResult<IReadOnlyList<OrderDto>?>(value);
        }

        Interlocked.Increment(ref Misses);
        return Task.FromResult<IReadOnlyList<OrderDto>?>(null);
    }

    public Task SetAsync(Guid userId, IReadOnlyList<OrderDto> orders, CancellationToken ct)
    {
        _store[RedisOrderListCache.Key(userId)] = orders;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(Guid userId, CancellationToken ct)
    {
        Interlocked.Increment(ref Invalidations);
        _store.TryRemove(RedisOrderListCache.Key(userId), out _);
        return Task.CompletedTask;
    }
}

public sealed class OrderApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "test-issuer";
    public const string Audience = "test-audience";
    public const string SigningKey = "test-signing-key-test-signing-key-0123456789";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeInventoryClient Inventory { get; } = new();
    public FakeOrderListCache Cache { get; } = new();

    public OrderApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:OrderDb", "Host=unused;Database=unused");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Inventory:BaseUrl", "http://inventory.invalid");
        builder.UseSetting("Internal:ApiKey", "test-internal-key");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<OrderDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<OrderDbContext>));
            services.AddDbContext<OrderDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<IInventoryClient>();
            services.AddSingleton<IInventoryClient>(Inventory);
            services.RemoveAll<IOrderListCache>();
            services.AddSingleton<IOrderListCache>(Cache);
        });
    }

    public HttpClient CreateClientFor(Guid userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, roles));
        return client;
    }

    public static string CreateToken(
        Guid userId, string[] roles, string signingKey = SigningKey, TimeSpan? lifetime = null)
    {
        var claims = new Dictionary<string, object> { ["userId"] = userId.ToString(), ["role"] = roles };
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(10));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = expires < now ? expires.AddMinutes(-5) : now,
            IssuedAt = expires < now ? expires.AddMinutes(-5) : now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
