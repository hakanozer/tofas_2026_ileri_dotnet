using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;

namespace EvolutionaryLogistics.Gateway.Middleware;

/// <summary>
/// Jaeger'daki trace ile birebir aynı TraceId'yi her cevabın <c>X-Trace-Id</c> header'ına ve
/// (JSON nesne cevaplarda) gövdedeki <c>traceId</c> alanına yazar; Postman'de gördüğünüz değer Jaeger'da aranabilir.
///
/// GOTCHA: Gövde değiştirmek cevabı belleğe almayı gerektirir. SSE/WebSocket/gRPC ve
/// <see cref="MaxBodyBytes"/> üstü cevaplar değiştirilmeden geçirilir (yalnız header eklenir).
/// </summary>
public sealed class TraceIdResponseMiddleware(RequestDelegate next, ILogger<TraceIdResponseMiddleware> logger)
{
    public const string HeaderName = "X-Trace-Id";
    public const string BodyField = "traceId";
    private const int MaxBodyBytes = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.Headers[HeaderName] = traceId;
        // Exception handler/DeveloperExceptionPage yanıtı Clear() eder; header'ı gönderim anında yeniden yaz.
        context.Response.OnStarting(static state =>
        {
            var (ctx, id) = ((HttpContext, string))state;
            ctx.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        }, (context, traceId));
        if (!ShouldRewriteBody(context.Request))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>();
        await using var buffer = new MemoryStream();
        // Buffer feature'ı StartAsync'i yutar: header'lar gövde yeniden yazılana kadar istemciye gitmez.
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(buffer));

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Features.Set(originalFeature);
        }

        var payload = buffer.TryGetBuffer(out var segment) ? segment : default;
        var rewritten = TryInjectTraceId(context.Response, payload, traceId);

        if (rewritten is not null)
        {
            context.Response.ContentLength = rewritten.Length;
            await originalBody.WriteAsync(rewritten, context.RequestAborted).ConfigureAwait(false);
        }
        else if (payload.Count > 0)
        {
            await originalBody.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static bool ShouldRewriteBody(HttpRequest request)
        => !HttpMethods.IsHead(request.Method)
           && !request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)
           && !request.Headers.Upgrade.Any()
           && request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) != true;

    private byte[]? TryInjectTraceId(HttpResponse response, ArraySegment<byte> payload, string traceId)
    {
        if (payload.Count is 0 or > MaxBodyBytes) return null;
        if (response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status304NotModified) return null;
        if (!string.IsNullOrEmpty(response.Headers.ContentEncoding)) return null;

        var contentType = response.ContentType;
        if (contentType is null
            || !(contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                 || contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase)))
            return null;

        try
        {
            if (JsonNode.Parse(payload) is not JsonObject json) return null;
            json[BodyField] = traceId;
            return JsonSerializer.SerializeToUtf8Bytes(json);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Response body is not valid JSON; traceId only added to header.");
            return null;
        }
    }
}

public static class TraceIdResponseExtensions
{
    public static IApplicationBuilder UseTraceIdResponse(this IApplicationBuilder app)
        => app.UseMiddleware<TraceIdResponseMiddleware>();
}
