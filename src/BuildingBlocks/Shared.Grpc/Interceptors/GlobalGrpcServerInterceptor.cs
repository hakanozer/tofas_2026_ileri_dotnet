using System.Diagnostics;
using EvolutionaryLogistics.BuildingBlocks.Grpc.Authentication;
using Microsoft.Extensions.Logging;
using EvolutionaryLogistics.BuildingBlocks.Kernel.Grpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace EvolutionaryLogistics.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Server-side pipeline: W3C trace extraction, JWT → metadata claims, deadline enforcement, linked OTEL spans.
/// </summary>
public sealed class GlobalGrpcServerInterceptor(
    IHttpContextAccessor httpContextAccessor,
    JwtMetadataParser jwtParser,
    ILogger<GlobalGrpcServerInterceptor> logger) : Interceptor
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        using var activity = StartServerActivity(context);
        EnrichActivity(activity, context);

        var metadata = context.RequestHeaders;
        var httpContext = httpContextAccessor.HttpContext;

        PropagateTraceFromMetadata(metadata, activity);

        var authHeader = metadata.GetValue(GrpcMetadataKeys.Authorization)
                         ?? httpContext?.Request.Headers.Authorization.ToString();

        var principal = jwtParser.TryParseBearer(authHeader);
        // NOT: `httpContext?.Items[...] = ...` (C# 14 null-conditional assignment) yerine
        // açık null kontrolü — IDE'nin dil sunucusu bu sözdizimini desteklemeyebiliyor.
        if (httpContext is not null)
        {
            httpContext.Items["GrpcCallContext"] = principal is not null
                ? BuildContextFromPrincipal(principal, metadata, httpContext)
                : JwtMetadataParser.ReadContextFromMetadata(metadata);
        }

        EnforceDeadline(context, httpContext?.Items["GrpcCallContext"] as GrpcCallContext);

        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "gRPC unary failed {Method}", context.Method);
            throw;
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var activity = StartServerActivity(context);
        PropagateTraceFromMetadata(context.RequestHeaders, activity);
        EnforceDeadline(context, null);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    private static Activity? StartServerActivity(ServerCallContext context)
    {
        var activity = new Activity("grpc.server");
        activity.SetTag("rpc.system", "grpc");
        activity.SetTag("rpc.service", context.Method?.Split('/').ElementAtOrDefault(1));
        activity.SetTag("rpc.method", context.Method?.Split('/').ElementAtOrDefault(2));
        activity.Start();
        return activity;
    }

    private static void EnrichActivity(Activity? activity, ServerCallContext context)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("net.peer.ip", context.Peer);
        activity.SetTag("grpc.status_code", context.Status.StatusCode.ToString());
    }

    private static void PropagateTraceFromMetadata(Metadata metadata, Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        var parentContext = Propagator.Extract(default, metadata, static (headers, key) =>
        {
            var value = headers.GetValue(key);
            return value is null ? [] : [value];
        });

        if (parentContext.ActivityContext.IsValid())
        {
            activity.SetParentId(
                parentContext.ActivityContext.TraceId,
                parentContext.ActivityContext.SpanId,
                parentContext.ActivityContext.TraceFlags);
        }

        var traceParent = metadata.GetValue(GrpcMetadataKeys.TraceParent);
        if (!string.IsNullOrEmpty(traceParent))
        {
            activity.SetTag("grpc.traceparent", traceParent);
        }
    }

    private static GrpcCallContext BuildContextFromPrincipal(
        System.Security.Claims.ClaimsPrincipal principal,
        Metadata metadata,
        HttpContext? httpContext)
    {
        var correlation = JwtMetadataParser.ResolveCorrelationId(httpContext, metadata);
        var ctx = JwtMetadataParser.ReadContextFromMetadata(metadata);
        return new GrpcCallContext
        {
            CorrelationId = correlation,
            TenantId = ctx.TenantId ?? principal.FindFirst("tenant_id")?.Value,
            UserId = ctx.UserId ?? principal.FindFirst("sub")?.Value,
            Roles = ctx.Roles.Count > 0
                ? ctx.Roles
                : principal.FindAll("role").Select(c => c.Value).ToArray(),
            DeadlineUtc = ctx.DeadlineUtc
        };
    }

    private static void EnforceDeadline(ServerCallContext context, GrpcCallContext? callContext)
    {
        if (context.Deadline != DateTime.MaxValue)
        {
            return;
        }

        if (callContext?.DeadlineUtc is { } deadline && deadline < DateTimeOffset.UtcNow)
        {
            throw new RpcException(new Status(StatusCode.DeadlineExceeded, "x-deadline-utc already elapsed"));
        }
    }

}
