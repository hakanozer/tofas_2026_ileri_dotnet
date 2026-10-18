using EvolutionaryLogistics.BuildingBlocks.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace EvolutionaryLogistics.Services.Ai.Guardrails;

/// <summary>Çağıranın kimliği: gateway'in enjekte ettiği başlıklardan doldurulur.</summary>
public sealed record UserContext(string UserId, string TenantId, IReadOnlySet<string> Roles);

/// <summary>
/// Kullanıcı bağlamını <see cref="AsyncLocal{T}"/> ile taşır. <c>IHttpContextAccessor</c>'a
/// gerek yoktur: SK tool filtreleri HTTP dışında (arka plan işleri) da çalışabilir.
/// </summary>
public static class UserContextAccessor
{
    private static readonly AsyncLocal<UserContext?> CurrentContext = new();

    public static UserContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}

/// <summary>Tool bazlı yetkilendirme sözleşmesi.</summary>
public interface IToolAllowList
{
    bool IsAllowed(UserContext user, string toolName);
}

/// <summary>
/// Rol → tool eşlemesi. Bilinmeyen tool varsayılan olarak REDDEDİLİR (fail-closed).
/// </summary>
public sealed class RoleBasedToolAllowList : IToolAllowList
{
    private static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["get_inventory_level"] = new(StringComparer.OrdinalIgnoreCase) { "reader", "ops", "admin" },
        ["forecast_stockout"] = new(StringComparer.OrdinalIgnoreCase) { "reader", "ops", "admin" },
        ["get_order_status"] = new(StringComparer.OrdinalIgnoreCase) { "support", "ops", "admin" }
    };

    public bool IsAllowed(UserContext user, string toolName)
    {
        if (!Map.TryGetValue(toolName, out var allowedRoles))
        {
            return false;
        }

        return user.Roles.Any(allowedRoles.Contains);
    }
}

/// <summary>Yetki kararlarının denetim kaydı (audit).</summary>
public interface IAuditLog
{
    void LogDenied(string userId, string toolName);
    void LogAllowed(string userId, string toolName);
}

/// <summary>Audit'i yapılandırılmış log üzerinden yazar.</summary>
public sealed class LoggerAuditLog(ILogger<LoggerAuditLog> logger) : IAuditLog
{
    public void LogDenied(string userId, string toolName) =>
        logger.LogWarning("Tool erişimi REDDEDİLDİ. userId={UserId}, tool={ToolName}", userId, toolName);

    public void LogAllowed(string userId, string toolName) =>
        logger.LogInformation("Tool erişimi onaylandı. userId={UserId}, tool={ToolName}", userId, toolName);
}

/// <summary>
/// Her kernel fonksiyon çağrısından önce yetki kontrolü yapar, sonra sonucu kırpar.
/// Sonuç kırpma (8000 karakter) veri sızdırma (exfiltration) savunmasıdır: model bir tool'dan
/// dev bir çıktı çekip prompt'a gömmeden önce boyut sınırlanır.
/// </summary>
public sealed class ToolAuthorizationFilter(IToolAllowList allowList, IAuditLog audit) : IFunctionInvocationFilter
{
    private const int MaxResultLength = 8000;

    private static readonly UserContext Anonymous =
        new("anonymous", "unknown", new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        var user = UserContextAccessor.Current ?? Anonymous;
        var toolName = context.Function.Name;

        if (!allowList.IsAllowed(user, toolName))
        {
            audit.LogDenied(user.UserId, toolName);
            ObservabilityNames.ToolInvocations.Add(
                1,
                new KeyValuePair<string, object?>("tool", toolName),
                new KeyValuePair<string, object?>("status", "denied"));
            throw new KernelException($"Tool '{toolName}' yetkisiz.");
        }

        audit.LogAllowed(user.UserId, toolName);
        await next(context);

        if (context.Result is { } result)
        {
            var text = result.ToString();
            if (text.Length > MaxResultLength)
            {
                context.Result = new FunctionResult(result, text[..MaxResultLength]);
            }
        }
    }
}
