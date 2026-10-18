using System.ComponentModel;
using EvolutionaryLogistics.BuildingBlocks.Observability;
using EvolutionaryLogistics.Services.Ai.Contracts;
using EvolutionaryLogistics.Services.Ai.Services;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;

namespace EvolutionaryLogistics.Services.Ai.Tools;

/// <summary>
/// Envanter tool'ları. Aynı metot iki farklı yüzeyden (yüzey) çağrılabilir:
/// <list type="bullet">
///   <item>MCP istemcileri: <c>[McpServerTool]</c> ile keşfedilir (call_tool).</item>
///   <item>Semantic Kernel / LLM: <c>[KernelFunction]</c> ile auto-function-calling'e girer.</item>
/// </list>
/// Her iki attribute da <c>Name</c> özelliğini destekler; isimler bilinçli olarak aynı tutuldu ki
/// guardrail allow-list tek yerden yönetilebilsin.
/// </summary>
[McpServerToolType]
public sealed class InventoryTools(IInventoryQueryService inventory)
{
    [KernelFunction("get_inventory_level")]
    [McpServerTool(Name = "get_inventory_level", ReadOnly = true)]
    [Description("Bir SKU için güncel stok seviyesini ve bulunduğu depoyu döner.")]
    public async Task<InventoryLevel> GetInventoryLevelAsync(
        [Description("Stok kodu, örn. SKU-1234 (^[A-Z0-9\\-]{3,32}$).")] string sku,
        CancellationToken ct)
    {
        using var activity = ObservabilityNames.AiPipeline.StartActivity("tool.call");
        activity?.SetTag("tool.name", "get_inventory_level");

        try
        {
            var level = await inventory.GetLevelAsync(sku, ct);
            Record("get_inventory_level", "ok");
            return level;
        }
        catch
        {
            Record("get_inventory_level", "error");
            throw;
        }
    }

    [KernelFunction("forecast_stockout")]
    [McpServerTool(Name = "forecast_stockout", ReadOnly = true)]
    [Description("Bir SKU için tahmini stok tükenme tarihini ve günlük tüketim hızını döner.")]
    public async Task<StockoutForecast> ForecastStockoutAsync(
        [Description("Stok kodu, örn. SKU-1234 (^[A-Z0-9\\-]{3,32}$).")] string sku,
        CancellationToken ct)
    {
        using var activity = ObservabilityNames.AiPipeline.StartActivity("tool.call");
        activity?.SetTag("tool.name", "forecast_stockout");

        try
        {
            var forecast = await inventory.ForecastAsync(sku, ct);
            Record("forecast_stockout", "ok");
            return forecast;
        }
        catch
        {
            Record("forecast_stockout", "error");
            throw;
        }
    }

    // Bounded label kuralı: tool adı ve durum (ok/error). SKU/tenant ASLA label olmaz.
    private static void Record(string tool, string status) =>
        ObservabilityNames.ToolInvocations.Add(
            1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("status", status));
}
