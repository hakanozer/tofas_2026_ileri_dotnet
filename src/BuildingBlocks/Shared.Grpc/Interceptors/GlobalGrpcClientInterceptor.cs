using System.Diagnostics;
using EvolutionaryLogistics.BuildingBlocks.Grpc.Authentication;
using EvolutionaryLogistics.BuildingBlocks.Kernel.Grpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace EvolutionaryLogistics.BuildingBlocks.Grpc.Interceptors;

/// <summary>
/// Client-side: inject JWT claims + W3C trace context + deadline into outgoing metadata; honor CallOptions.Deadline.
/// </summary>
public sealed class GlobalGrpcClientInterceptor(
    IHttpContextAccessor httpContextAccessor,
    JwtMetadataParser jwtParser) : Interceptor
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = PrepareHeaders(context.Options.Headers);
        var options = context.Options.WithHeaders(headers);
        if (ResolveDeadline(context.Options.Deadline, headers) is { } deadline
            && deadline != context.Options.Deadline)
        {
            options = options.WithDeadline(deadline);
        }

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            options);

        return continuation(request, newContext);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = PrepareHeaders(context.Options.Headers);
        var options = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            options);

        return continuation(request, newContext);
    }

    private Metadata PrepareHeaders(Metadata? existing)
    {
        var metadata = existing is not null ? existing : [];

        var httpContext = httpContextAccessor.HttpContext;
        var correlation = JwtMetadataParser.ResolveCorrelationId(httpContext, metadata);
        SetIfMissing(metadata, GrpcMetadataKeys.CorrelationId, correlation);

        var auth = httpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth))
        {
            SetIfMissing(metadata, GrpcMetadataKeys.Authorization, auth);
        }

        var principal = jwtParser.TryParseBearer(auth);
        if (principal is not null)
        {
            JwtMetadataParser.InjectClaimsIntoMetadata(metadata, principal, correlation);
        }

        InjectTraceContext(metadata);

        if (httpContext?.Items["GrpcCallContext"] is GrpcCallContext callContext
            && callContext.DeadlineUtc is { } propagatedDeadline)
        {
            SetIfMissing(metadata, GrpcMetadataKeys.DeadlineUtc, propagatedDeadline.ToString("O"));
        }

        return metadata;
    }

    private static void InjectTraceContext(Metadata metadata)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        Propagator.Inject(
            new PropagationContext(activity.Context, baggage: default),
            metadata,
            static (carrier, key, value) => carrier.Add(key, value));

        SetIfMissing(metadata, GrpcMetadataKeys.TraceParent, activity.Id ?? string.Empty);
    }

    private static DateTime? ResolveDeadline(DateTime? currentDeadline, Metadata metadata)
    {
        if (currentDeadline is not null)
        {
            return currentDeadline;
        }

        var raw = metadata.GetValue(GrpcMetadataKeys.DeadlineUtc);
        if (DateTimeOffset.TryParse(raw, out var utc))
        {
            return utc.UtcDateTime;
        }

        return null;
    }

    private static void SetIfMissing(Metadata metadata, string key, string value)
    {
        if (metadata.GetValue(key) is not null || string.IsNullOrEmpty(value))
        {
            return;
        }

        metadata.Add(key, value);
    }
}
