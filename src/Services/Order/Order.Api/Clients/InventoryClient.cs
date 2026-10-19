using System.Net;
using System.Net.Http.Json;
using Order.Api.Caching;

namespace Order.Api.Clients;

public sealed class InventoryClientOptions
{
    public const string Section = "Inventory";
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed record StockShortage(string Sku, int Requested, int Available);

public enum InventoryOutcome { Reserved, Insufficient, Unavailable }

public sealed record InventoryReserveResult(InventoryOutcome Outcome, IReadOnlyList<StockShortage> Shortages);

public interface IInventoryClient
{
    Task<InventoryReserveResult> ReserveAsync(Guid orderId, IReadOnlyList<OrderItemDto> items, CancellationToken ct);
    Task ReleaseAsync(Guid orderId, CancellationToken ct);
}

public sealed class InventoryClient(HttpClient http, ILogger<InventoryClient> logger) : IInventoryClient
{
    private sealed record ReserveResponseBody(string? Status, List<StockShortage>? Shortages);

    public async Task<InventoryReserveResult> ReserveAsync(Guid orderId, IReadOnlyList<OrderItemDto> items, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("internal/reservations",
                new { orderId, items = items.Select(i => new { sku = i.Sku, quantity = i.Quantity }) }, ct);

            if (response.StatusCode == HttpStatusCode.OK)
                return new InventoryReserveResult(InventoryOutcome.Reserved, []);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var body = await response.Content.ReadFromJsonAsync<ReserveResponseBody>(ct);
                return new InventoryReserveResult(InventoryOutcome.Insufficient, body?.Shortages ?? []);
            }

            logger.LogError("Inventory reservation for order {OrderId} failed with HTTP {Status}", orderId, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogError(ex, "Inventory service call failed for order {OrderId}", orderId);
        }

        return new InventoryReserveResult(InventoryOutcome.Unavailable, []);
    }

    public async Task ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        try
        {
            using var response = await http.DeleteAsync($"internal/reservations/{orderId}", ct);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                logger.LogWarning("Inventory release for order {OrderId} returned HTTP {Status}", orderId, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Inventory release failed for order {OrderId}", orderId);
        }
    }
}
