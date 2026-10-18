using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grpc.Core;
using Microsoft.IdentityModel.Tokens;
using EvolutionaryLogistics.BuildingBlocks.Kernel.Grpc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EvolutionaryLogistics.BuildingBlocks.Grpc.Authentication;

public sealed class JwtMetadataParser(ILogger<JwtMetadataParser> logger)
{
    public ClaimsPrincipal? TryParseBearer(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (token.Length == 0)
        {
            return null;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            if (!handler.CanReadToken(token))
            {
                return null;
            }

            var jwt = handler.ReadJwtToken(token);
            var identity = new ClaimsIdentity(jwt.Claims, authenticationType: "Bearer");
            return new ClaimsPrincipal(identity);
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            logger.LogDebug(ex, "JWT parse failed — downstream must reject if auth required");
            return null;
        }
    }

    public static void InjectClaimsIntoMetadata(Metadata metadata, ClaimsPrincipal principal, string correlationId)
    {
        metadata.Add(GrpcMetadataKeys.CorrelationId, correlationId);

        var sub = principal.FindFirst("sub")?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(sub))
        {
            metadata.Add(GrpcMetadataKeys.UserId, sub);
        }

        var tenant = principal.FindFirst("tenant_id")?.Value
                     ?? principal.FindFirst("tid")?.Value;
        if (!string.IsNullOrEmpty(tenant))
        {
            metadata.Add(GrpcMetadataKeys.TenantId, tenant);
        }

        var roles = principal.FindAll("role")
            .Concat(principal.FindAll(ClaimTypes.Role))
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roles.Length > 0)
        {
            metadata.Add(GrpcMetadataKeys.UserRoles, string.Join(',', roles));
        }
    }

    public static GrpcCallContext ReadContextFromMetadata(Metadata metadata)
    {
        var correlation = metadata.GetValue(GrpcMetadataKeys.CorrelationId)
                          ?? Guid.NewGuid().ToString("N");

        DateTimeOffset? deadline = null;
        var deadlineRaw = metadata.GetValue(GrpcMetadataKeys.DeadlineUtc);
        if (DateTimeOffset.TryParse(deadlineRaw, out var parsed))
        {
            deadline = parsed;
        }

        var rolesRaw = metadata.GetValue(GrpcMetadataKeys.UserRoles);
        var roles = string.IsNullOrEmpty(rolesRaw)
            ? Array.Empty<string>()
            : rolesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new GrpcCallContext
        {
            CorrelationId = correlation,
            TenantId = metadata.GetValue(GrpcMetadataKeys.TenantId),
            UserId = metadata.GetValue(GrpcMetadataKeys.UserId),
            Roles = roles,
            DeadlineUtc = deadline
        };
    }

    public static string ResolveCorrelationId(HttpContext? httpContext, Metadata? metadata)
    {
        if (httpContext?.Request.Headers.TryGetValue(GrpcMetadataKeys.CorrelationId, out var header) == true
            && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        return metadata?.GetValue(GrpcMetadataKeys.CorrelationId) ?? Guid.NewGuid().ToString("N");
    }
}
