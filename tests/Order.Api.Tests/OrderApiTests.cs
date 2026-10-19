using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Order.Api.Caching;
using Order.Api.Clients;
using Order.Api.Data;

namespace Order.Api.Tests;

public sealed class OrderApiTests : IClassFixture<OrderApiFactory>
{
    private static readonly object[] OneItem = [new { sku = "SKU-1001", quantity = 2 }];

    private readonly OrderApiFactory _factory;

    public OrderApiTests(OrderApiFactory factory)
    {
        _factory = factory;
        _factory.Inventory.Result = new InventoryReserveResult(InventoryOutcome.Reserved, []);
    }

    private static Guid NewUser() => Guid.NewGuid();

    // ── Başarılı sipariş ────────────────────────────────────────────────────
    [Fact]
    public async Task CreateOrder_WithEnoughStock_IsConfirmed()
    {
        var user = NewUser();
        using var client = _factory.CreateClientFor(user, "order");

        var response = await client.PostAsJsonAsync("/orders", new { items = OneItem });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal("Confirmed", order!.Status);
        Assert.Single(order.Items);
        Assert.Contains(order.Id, _factory.Inventory.Reserved);
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task CreateOrder_IgnoresUserIdInBody_AndUsesTokenUser()
    {
        var attacker = NewUser();
        var victim = NewUser();
        using var client = _factory.CreateClientFor(attacker, "order");

        var response = await client.PostAsJsonAsync("/orders", new { userId = victim, items = OneItem });
        var created = await response.Content.ReadFromJsonAsync<OrderDto>();

        using var victimClient = _factory.CreateClientFor(victim, "order");
        Assert.Empty((await victimClient.GetFromJsonAsync<List<OrderDto>>("/orders"))!);
        Assert.Contains((await client.GetFromJsonAsync<List<OrderDto>>("/orders"))!, o => o.Id == created!.Id);
    }

    // ── Yetersiz stok ───────────────────────────────────────────────────────
    [Fact]
    public async Task CreateOrder_WithInsufficientStock_IsRejected()
    {
        _factory.Inventory.Result = new InventoryReserveResult(
            InventoryOutcome.Insufficient, [new StockShortage("SKU-1001", 2, 1)]);
        var user = NewUser();
        using var client = _factory.CreateClientFor(user, "order");

        var response = await client.PostAsJsonAsync("/orders", new { items = OneItem });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal("Rejected", order!.Status);
        Assert.Contains("SKU-1001", order.RejectionReason);

        var persisted = await client.GetFromJsonAsync<OrderDto>($"/orders/{order.Id}");
        Assert.Equal("Rejected", persisted!.Status);
    }

    [Fact]
    public async Task CreateOrder_WhenInventoryUnavailable_IsRejectedWith503_AndReleasesReservation()
    {
        _factory.Inventory.Result = new InventoryReserveResult(InventoryOutcome.Unavailable, []);
        using var client = _factory.CreateClientFor(NewUser(), "order");

        var response = await client.PostAsJsonAsync("/orders", new { items = OneItem });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal("Rejected", order!.Status);
        Assert.Contains(order.Id, _factory.Inventory.Released);
    }

    // ── Validation ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("{\"items\":[]}")]
    [InlineData("{}")]
    [InlineData("{\"items\":[{\"sku\":\"\",\"quantity\":1}]}")]
    [InlineData("{\"items\":[{\"sku\":\"A\",\"quantity\":0}]}")]
    [InlineData("{\"items\":[{\"sku\":\"A\",\"quantity\":-5}]}")]
    public async Task CreateOrder_WithInvalidBody_ReturnsBadRequest(string json)
    {
        using var client = _factory.CreateClientFor(NewUser(), "order");
        var response = await client.PostAsync("/orders", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── JWT olmadan / hatalı JWT ile erişim ─────────────────────────────────
    [Fact]
    public async Task Endpoints_WithoutJwt_Return401()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/orders/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/orders", new { items = OneItem })).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithTamperedOrExpiredJwt_Return401()
    {
        var user = NewUser();
        var wrongKey = OrderApiFactory.CreateToken(user, ["order"], signingKey: "another-signing-key-another-signing-key-99");
        var expired = OrderApiFactory.CreateToken(user, ["order"], lifetime: TimeSpan.FromMinutes(-30));

        foreach (var token in new[] { wrongKey, expired })
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/orders")).StatusCode);
        }
    }

    [Fact]
    public async Task Endpoints_WithoutOrderRole_Return403()
    {
        using var client = _factory.CreateClientFor(NewUser(), "inventory");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/orders")).StatusCode);
    }

    // ── Başka kullanıcının siparişi ─────────────────────────────────────────
    [Fact]
    public async Task GetOrder_OfAnotherUser_ReturnsNotFound()
    {
        using var owner = _factory.CreateClientFor(NewUser(), "order");
        var created = await (await owner.PostAsJsonAsync("/orders", new { items = OneItem }))
            .Content.ReadFromJsonAsync<OrderDto>();

        using var other = _factory.CreateClientFor(NewUser(), "order");

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/orders/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/orders/{created.Id}")).StatusCode);
        Assert.DoesNotContain((await other.GetFromJsonAsync<List<OrderDto>>("/orders"))!, o => o.Id == created.Id);
    }

    // ── Redis cache: hit / miss / invalidation / izolasyon ─────────────────
    [Fact]
    public async Task ListOrders_FirstCallMisses_SecondCallHits_ServedFromCache()
    {
        var user = NewUser();
        using var client = _factory.CreateClientFor(user, "order");
        await client.PostAsJsonAsync("/orders", new { items = OneItem });
        var (hits, misses) = (_factory.Cache.Hits, _factory.Cache.Misses);

        var first = await client.GetFromJsonAsync<List<OrderDto>>("/orders");
        Assert.Equal(misses + 1, _factory.Cache.Misses);
        Assert.True(_factory.Cache.Contains(user));

        // DB'ye cache'i atlayarak sipariş ekle: ikinci çağrı DB'ye gitseydi bunu görürdü.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            db.Orders.Add(new CustomerOrder { Id = Guid.NewGuid(), UserId = user, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var second = await client.GetFromJsonAsync<List<OrderDto>>("/orders");
        Assert.Equal(hits + 1, _factory.Cache.Hits);
        Assert.Equal(first!.Count, second!.Count);
    }

    [Fact]
    public async Task CreateOrder_InvalidatesOnlyOwnersCache()
    {
        var alice = NewUser();
        var bob = NewUser();
        using var aliceClient = _factory.CreateClientFor(alice, "order");
        using var bobClient = _factory.CreateClientFor(bob, "order");

        await aliceClient.GetFromJsonAsync<List<OrderDto>>("/orders");
        await bobClient.GetFromJsonAsync<List<OrderDto>>("/orders");
        Assert.True(_factory.Cache.Contains(alice));
        Assert.True(_factory.Cache.Contains(bob));

        await aliceClient.PostAsJsonAsync("/orders", new { items = OneItem });

        Assert.False(_factory.Cache.Contains(alice));
        Assert.True(_factory.Cache.Contains(bob));

        var aliceList = await aliceClient.GetFromJsonAsync<List<OrderDto>>("/orders");
        Assert.Single(aliceList!);
    }

    [Fact]
    public async Task OrderListCache_UsesPerUserKeys_AndNeverSharesData()
    {
        var alice = NewUser();
        var bob = NewUser();
        using var aliceClient = _factory.CreateClientFor(alice, "order");
        using var bobClient = _factory.CreateClientFor(bob, "order");
        await aliceClient.PostAsJsonAsync("/orders", new { items = OneItem });

        var aliceList = await aliceClient.GetFromJsonAsync<List<OrderDto>>("/orders");
        var bobList = await bobClient.GetFromJsonAsync<List<OrderDto>>("/orders");

        Assert.Single(aliceList!);
        Assert.Empty(bobList!);
        Assert.Equal($"orders:user:{alice}:list", RedisOrderListCache.Key(alice));
        Assert.Contains(RedisOrderListCache.Key(alice), _factory.Cache.Keys);
        Assert.Contains(RedisOrderListCache.Key(bob), _factory.Cache.Keys);
    }
}
