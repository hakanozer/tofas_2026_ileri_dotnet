using StackExchange.Redis;

namespace EvolutionaryLogistics.Gateway.Middleware;

/// <summary>
/// Per-tenant günlük LLM token kotası. Redis üzerinde atomik sayaç tutar.
///
/// GOTCHA: Quota'yı process-içi bellekte tutarsanız, gateway yatay ölçeklendiğinde
/// her replika kendi kotasını uygular → gerçek limitin N katı harcanır. Bu yüzden
/// kaynak-of-truth Redis'tir (INCR/DECR atomik).
/// </summary>
public interface ITenantQuotaStore
{
    /// <summary>Tenant'ın bugün kalan token kotası. Negatifse 0 döner.</summary>
    Task<long> GetRemainingAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Kullanılan token'ı düşer. Redis erişilemezse sessizce geçer (fail-open).</summary>
    Task RecordUsageAsync(string tenantId, long tokens, CancellationToken ct = default);
}

/// <summary>Redis tabanlı kota deposu. Günlük pencere (UTC gece yarısı sıfırlanır).</summary>
public sealed class RedisTenantQuotaStore(
    IConnectionMultiplexer redis,
    ILogger<RedisTenantQuotaStore> logger) : ITenantQuotaStore
{
    /// <summary>Tenant başına günlük token bütçesi. Gerçekte plan/tier'a göre konfigüre edilir.</summary>
    private const long DailyBudget = 100_000;

    private const string KeyPrefix = "quota:tokens:";

    public async Task<long> GetRemainingAsync(string tenantId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var used = await db.StringGetAsync(KeyPrefix + tenantId);
            var remaining = DailyBudget - (long)(used.HasValue ? (long)used : 0);
            return remaining < 0 ? 0 : remaining;
        }
        catch (RedisException ex)
        {
            // Fail-open: kota deposu düşse bile trafiği kesmeyiz — aksi halde
            // Redis kesintisi tüm AI özelliğini durdurur (availability > cost control).
            logger.LogWarning(ex, "Quota store unreachable for tenant={Tenant} — failing open", tenantId);
            return DailyBudget;
        }
    }

    public async Task RecordUsageAsync(string tenantId, long tokens, CancellationToken ct = default)
    {
        if (tokens <= 0)
        {
            return;
        }

        try
        {
            var db = redis.GetDatabase();
            var key = KeyPrefix + tenantId;
            var total = await db.StringIncrementAsync(key, tokens);

            if (total == tokens)
            {
                // İlk yazımda TTL kur — günlük pencere. TTL olmadan anahtarlar sonsuza büyür.
                var untilMidnight = DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
                await db.KeyExpireAsync(key, untilMidnight);
            }
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Quota usage write failed for tenant={Tenant}", tenantId);
        }
    }
}

/// <summary>
/// Yanıt gövdesini sayaçla sarar. Yazılan byte'ları geçirirken token tahmini toplar.
/// NOT: Token sayımı tahminîdir (chars/4) — gerçek tokenizer değil. Ücret mutabakatı
/// için sağlayıcının usage alanını kullanın; bu sayaç runtime bütçe kontrolü içindir.
/// </summary>
public sealed class TokenCountStream(Stream inner) : Stream
{
    private long _bytesWritten;

    /// <summary>Tahmini yazılan token sayısı (chars/4).</summary>
    public long TokensWritten => _bytesWritten / 4;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        Interlocked.Add(ref _bytesWritten, count);
        inner.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Interlocked.Add(ref _bytesWritten, buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}

/// <summary>
/// LLM token bütçesi kapısı. Bütçe tükendiyse isteği modele HİÇ göndermeden 429 döner (sıfır maliyet).
/// Streaming (SSE) yanıtlarında token sayacı yanıt gövdesini sarar.
/// </summary>
public sealed class LlmTokenBudgetMiddleware(
    RequestDelegate next,
    ITenantQuotaStore quotas,
    ILogger<LlmTokenBudgetMiddleware> logger)
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string BudgetHeader = "X-Token-Budget";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/ai", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var tenant = context.Request.Headers[TenantHeader].FirstOrDefault()
                     ?? context.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrWhiteSpace(tenant))
        {
            // Tenant kimliği olmadan bütçe uygulanamaz; anonim trafiği sayamayız.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"{TenantHeader} header zorunlu.").ConfigureAwait(false);
            return;
        }

        var remaining = await quotas.GetRemainingAsync(tenant, context.RequestAborted);
        if (remaining <= 0)
        {
            logger.LogWarning("Token budget exhausted tenant={Tenant} path={Path}", tenant, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsync("Token budget exhausted").ConfigureAwait(false);
            return;
        }

        context.Request.Headers[BudgetHeader] = remaining.ToString();

        var originalBody = context.Response.Body;
        var counter = new TokenCountStream(originalBody);
        context.Response.Body = counter;

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
            await quotas.RecordUsageAsync(tenant, counter.TokensWritten, context.RequestAborted);
            logger.LogDebug("LLM usage tenant={Tenant} tokens≈{Tokens}", tenant, counter.TokensWritten);
        }
    }
}

public static class LlmTokenBudgetExtensions
{
    public static IApplicationBuilder UseLlmTokenBudget(this IApplicationBuilder app)
        => app.UseMiddleware<LlmTokenBudgetMiddleware>();
}
