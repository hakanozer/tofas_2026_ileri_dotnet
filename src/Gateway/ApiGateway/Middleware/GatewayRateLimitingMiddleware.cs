using System.Threading.RateLimiting;
using EvolutionaryLogistics.BuildingBlocks.Kernel.Grpc;
using Yarp.ReverseProxy.Model;

namespace EvolutionaryLogistics.Gateway.Middleware;

/// <summary>
/// Maps YARP route metadata RateLimitPolicy → partitioned token buckets (tenant + route).
/// </summary>
public sealed class GatewayRateLimitingMiddleware(
    RequestDelegate next,
    ILogger<GatewayRateLimitingMiddleware> logger)
{
    private static readonly Dictionary<string, RateLimiter> Limiters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read-heavy"] = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 500,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 500,
            AutoReplenishment = true,
            QueueLimit = 100
        }),
        ["write-heavy"] = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 120,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 120,
            AutoReplenishment = true,
            QueueLimit = 30
        }),
        ["ai-token-budget"] = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 30,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 30,
            AutoReplenishment = true,
            QueueLimit = 5
        }),
        ["anonymous-burst"] = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 60,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 60,
            AutoReplenishment = true,
            QueueLimit = 0
        })
    };

    public async Task InvokeAsync(HttpContext context)
    {
        // GOTCHA: GetReverseProxyFeature() feature yoksa InvalidOperationException fırlatır —
        // bu yüzden Features.Get<> ile null-safe okunur. /health gibi proxy olmayan yollar
        // ve proxy endpoint'i henüz çalışmamış istekler sessizce geçer.
        var route = context.Features.Get<IReverseProxyFeature>()?.Route;
        if (route is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var policyName = route.Config.Metadata?.GetValueOrDefault("RateLimitPolicy") ?? "read-heavy";

        if (!Limiters.TryGetValue(policyName, out var limiter))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var partitionKey = context.Request.Headers[GrpcMetadataKeys.TenantId].FirstOrDefault()
                           ?? context.User.FindFirst("tenant_id")?.Value
                           ?? context.Connection.RemoteIpAddress?.ToString()
                           ?? "anonymous";

        using var lease = await limiter.AcquireAsync(permitCount: 1, context.RequestAborted).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            logger.LogWarning("Rate limit exceeded policy={Policy} partition={Partition} path={Path}",
                policyName, partitionKey, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsync("Gateway rate limit exceeded.").ConfigureAwait(false);
            return;
        }

        context.Response.Headers["X-RateLimit-Policy"] = policyName;
        await next(context).ConfigureAwait(false);
    }
}

public static class GatewayRateLimitingExtensions
{
    public static IApplicationBuilder UseGatewayRateLimiting(this IApplicationBuilder app)
        => app.UseMiddleware<GatewayRateLimitingMiddleware>();
}
