using System.Text.RegularExpressions;
using EvolutionaryLogistics.Services.Ai.Contracts;

namespace EvolutionaryLogistics.Services.Ai.Services;

/// <summary>Envanter sorgu sözleşmesi — tool'lar yalnızca bu typed arayüzle konuşur (SQL sızdırmaz).</summary>
public interface IInventoryQueryService
{
    Task<InventoryLevel> GetLevelAsync(string sku, CancellationToken ct);
    Task<StockoutForecast> ForecastAsync(string sku, CancellationToken ct);
}

/// <summary>Sipariş sorgu sözleşmesi.</summary>
public interface IOrderQueryService
{
    Task<OrderStatus> GetOrderStatusAsync(string orderId, CancellationToken ct);
}

/// <summary>
/// In-process demo envanter sorgusu.
///
/// Gün 2'de bu servis Inventory/Order gRPC client'ı ile değiştirilir; burada tool
/// sözleşmesini (typed, parametreli, SQL'siz) göstermek için in-process demo veri kullanılıyor.
/// Çıktı deterministic'tir (SKU hash'ine bağlı) — testler tekrarlanabilir kalır.
/// </summary>
public sealed class InMemoryInventoryQueryService : IInventoryQueryService
{
    private static readonly Regex SkuPattern = new(@"^[A-Z0-9\-]{3,32}$", RegexOptions.Compiled);
    private static readonly string[] Warehouses = ["IST-01", "ANK-02", "IZM-03", "ADB-04"];

    public Task<InventoryLevel> GetLevelAsync(string sku, CancellationToken ct)
    {
        ValidateSku(sku);
        var hash = StableHash(sku);
        var quantity = 10 + (int)(hash % 490);
        var warehouse = Warehouses[(int)(hash % (uint)Warehouses.Length)];
        return Task.FromResult(new InventoryLevel(sku, quantity, warehouse));
    }

    public Task<StockoutForecast> ForecastAsync(string sku, CancellationToken ct)
    {
        ValidateSku(sku);
        var hash = StableHash(sku);
        var velocity = Math.Round(1 + (hash % 100) / 10.0, 2);
        var days = 7 + (int)(hash % 30);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days);
        return Task.FromResult(new StockoutForecast(sku, date, velocity));
    }

    internal static void ValidateSku(string sku)
    {
        if (!SkuPattern.IsMatch(sku))
        {
            // Parametre doğrulaması: geçersiz girdi model katmanına sızmadan burada kesilir.
            throw new ArgumentException($"Geçersiz SKU '{sku}'. Beklenen biçim: ^[A-Z0-9\\-]{{3,32}}$", nameof(sku));
        }
    }

    // String.GetHashCode process'e özgü rastgeleleştirilir → deterministic hash için FNV-1a kullanılır.
    internal static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}

/// <summary>In-process demo sipariş sorgusu (Gün 2'de Order gRPC client'ı ile değiştirilir).</summary>
public sealed class InMemoryOrderQueryService : IOrderQueryService
{
    private static readonly Regex OrderIdPattern = new(@"^[A-Za-z0-9\-]{4,40}$", RegexOptions.Compiled);
    private static readonly string[] Statuses = ["Hazırlanıyor", "Kargoda", "Teslim Edildi", "İptal"];

    public Task<OrderStatus> GetOrderStatusAsync(string orderId, CancellationToken ct)
    {
        if (!OrderIdPattern.IsMatch(orderId))
        {
            throw new ArgumentException($"Geçersiz sipariş no '{orderId}'. Beklenen biçim: ^[A-Za-z0-9\\-]{{4,40}}$", nameof(orderId));
        }

        var hash = InMemoryInventoryQueryService.StableHash(orderId);
        var status = Statuses[(int)(hash % (uint)Statuses.Length)];
        var customerId = $"C-{1000 + hash % 9000}";
        return Task.FromResult(new OrderStatus(orderId, status, customerId));
    }
}
