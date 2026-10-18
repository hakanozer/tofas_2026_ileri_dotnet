using System.Security.Claims;
using EvolutionaryLogistics.BuildingBlocks.Kernel.Grpc;

namespace EvolutionaryLogistics.Gateway.Middleware;

/// <summary>
/// Ensures correlation id continuity and forwards JWT claims as headers for downstream gRPC/REST.
/// </summary>
public sealed class CorrelationAndClaimInjectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlation = context.Request.Headers[GrpcMetadataKeys.CorrelationId].FirstOrDefault()
                          ?? Guid.NewGuid().ToString("N");

        context.Request.Headers[GrpcMetadataKeys.CorrelationId] = correlation;
        context.Response.Headers[GrpcMetadataKeys.CorrelationId] = correlation;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            InjectClaimHeader(context, GrpcMetadataKeys.UserId, "sub", ClaimTypes.NameIdentifier);
            InjectClaimHeader(context, GrpcMetadataKeys.TenantId, "tenant_id", "tid");
            InjectRolesHeader(context);
        }

        await next(context).ConfigureAwait(false);
    }

    private static void InjectClaimHeader(HttpContext context, string header, params string[] claimTypes)
    {
        foreach (var type in claimTypes)
        {
            var value = context.User.FindFirst(type)?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                context.Request.Headers[header] = value;
                return;
            }
        }
    }

    private static void InjectRolesHeader(HttpContext context)
    {
        var roles = context.User.FindAll("role")
            .Concat(context.User.FindAll(ClaimTypes.Role))
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roles.Length > 0)
        {
            context.Request.Headers[GrpcMetadataKeys.UserRoles] = string.Join(',', roles);
        }
    }
}

public static class CorrelationAndClaimInjectionExtensions
{
    public static IApplicationBuilder UseCorrelationAndClaimInjection(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationAndClaimInjectionMiddleware>();
}
