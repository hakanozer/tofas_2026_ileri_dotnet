using System.Net;
using System.Net.Http.Json;

namespace Inventory.Api.Tests;

public sealed class InventoryApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public InventoryApiTests(InventoryApiFactory factory) => _factory = factory;

    private static string NewSku() => "SKU-" + Guid.NewGuid().ToString("N")[..8];

    private static object Reserve(Guid orderId, string sku, int quantity)
        => new { orderId, items = new[] { new { sku, quantity } } };

    [Fact]
    public async Task Reserve_WithEnoughStock_DecrementsAvailable()
    {
        var sku = NewSku();
        await _factory.SeedAsync(sku, 10);
        using var client = _factory.CreateInternalClient();

        var response = await client.PostAsJsonAsync("/internal/reservations", Reserve(Guid.NewGuid(), sku, 4));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stock = await _factory.GetStockAsync(sku);
        Assert.Equal(6, stock.Available);
        Assert.Equal(4, stock.Reserved);
    }

    [Fact]
    public async Task Reserve_WithInsufficientStock_Returns409_AndLeavesStockUntouched()
    {
        var enough = NewSku();
        var scarce = NewSku();
        await _factory.SeedAsync(enough, 10);
        await _factory.SeedAsync(scarce, 1);
        using var client = _factory.CreateInternalClient();

        var response = await client.PostAsJsonAsync("/internal/reservations", new
        {
            orderId = Guid.NewGuid(),
            items = new[] { new { sku = enough, quantity = 3 }, new { sku = scarce, quantity = 2 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(scarce, body);
        Assert.DoesNotContain($"\"{enough}\"", body);
        // Tüm-ya-da-hiç: yeterli olan kalem de düşülmemiş olmalı.
        Assert.Equal(10, (await _factory.GetStockAsync(enough)).Available);
        Assert.Equal(1, (await _factory.GetStockAsync(scarce)).Available);
    }

    [Fact]
    public async Task Reserve_UnknownSku_Returns409()
    {
        using var client = _factory.CreateInternalClient();
        var response = await client.PostAsJsonAsync("/internal/reservations", Reserve(Guid.NewGuid(), NewSku(), 1));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_SameOrderTwice_IsIdempotent()
    {
        var sku = NewSku();
        await _factory.SeedAsync(sku, 10);
        using var client = _factory.CreateInternalClient();
        var orderId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/internal/reservations", Reserve(orderId, sku, 4))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/internal/reservations", Reserve(orderId, sku, 4))).StatusCode);

        Assert.Equal(6, (await _factory.GetStockAsync(sku)).Available);
    }

    [Fact]
    public async Task Release_RestoresStock_AndIsIdempotent()
    {
        var sku = NewSku();
        await _factory.SeedAsync(sku, 10);
        using var client = _factory.CreateInternalClient();
        var orderId = Guid.NewGuid();
        await client.PostAsJsonAsync("/internal/reservations", Reserve(orderId, sku, 4));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/reservations/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/reservations/{orderId}")).StatusCode);

        var stock = await _factory.GetStockAsync(sku);
        Assert.Equal(10, stock.Available);
        Assert.Equal(0, stock.Reserved);
    }

    [Fact]
    public async Task InternalEndpoints_WithoutOrWithWrongApiKey_Return401()
    {
        using var anonymous = _factory.CreateClient();
        using var wrong = _factory.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Internal-Api-Key", "nope");
        var payload = Reserve(Guid.NewGuid(), NewSku(), 1);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/internal/reservations", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.PostAsJsonAsync("/internal/reservations", payload)).StatusCode);
    }

    [Fact]
    public async Task StockEndpoints_RequireJwtWithInventoryRole()
    {
        using var anonymous = _factory.CreateClient();
        using var orderOnly = _factory.CreateUserClient("order");
        using var inventoryUser = _factory.CreateUserClient("inventory");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await orderOnly.GetAsync("/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await inventoryUser.GetAsync("/stock")).StatusCode);
    }

    [Fact]
    public async Task SetStock_UpsertsAndValidates()
    {
        using var client = _factory.CreateUserClient("inventory");
        var sku = NewSku();

        var created = await client.PutAsJsonAsync($"/stock/{sku}", new { quantity = 7 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(7, (await _factory.GetStockAsync(sku)).Available);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/stock/{sku}", new { quantity = -1 })).StatusCode);
    }
}
