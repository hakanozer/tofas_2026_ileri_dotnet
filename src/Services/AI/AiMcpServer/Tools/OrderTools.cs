using System.ComponentModel;
using EvolutionaryLogistics.BuildingBlocks.Observability;
using EvolutionaryLogistics.Services.Ai.Contracts;
using EvolutionaryLogistics.Services.Ai.Services;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;

namespace EvolutionaryLogistics.Services.Ai.Tools;

/// <summary>
/// Sipariş tool'ları. MCP (<c>[McpServerTool]</c>) ve Semantic Kernel (<c>[KernelFunction]</c>)
/// yüzeylerini tek metotta birleştirir; guardrail allow-list bu isimlerle eşleşir.
/// </summary>
[McpServerToolType]
public sealed class OrderTools(IOrderQueryService orders)
{
    [KernelFunction("get_order_status")]
    [McpServerTool(Name = "get_order_status", ReadOnly = true)]
    [Description("Verilen sipariş numarası için güncel sipariş durumunu ve müşteri kimliğini döner.")]
    public async Task<OrderStatus> GetOrderStatusAsync(
        [Description("Sipariş numarası, örn. ORD-10023 (^[A-Za-z0-9\\-]{4,40}$).")] string orderId,
        CancellationToken ct)
    {
        using var activity = ObservabilityNames.AiPipeline.StartActivity("tool.call");
        activity?.SetTag("tool.name", "get_order_status");

        try
        {
            var status = await orders.GetOrderStatusAsync(orderId, ct);
            Record("get_order_status", "ok");
            return status;
        }
        catch
        {
            Record("get_order_status", "error");
            throw;
        }
    }

    private static void Record(string tool, string status) =>
        ObservabilityNames.ToolInvocations.Add(
            1,
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("status", status));
}
