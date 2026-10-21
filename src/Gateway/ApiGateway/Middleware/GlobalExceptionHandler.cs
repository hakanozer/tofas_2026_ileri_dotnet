using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EvolutionaryLogistics.Gateway.Middleware;

/// <summary>
/// İşlenmeyen exception'ları istemciye yalnızca basit bir ProblemDetails olarak döner.
/// Exception ayrıntısı (stack trace vb.) response'a sızmaz; log ve trace tarafında kalır.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // İstemci bağlantıyı kestiyse yanıt yazmanın anlamı yok.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var (status, title) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Geçersiz istek."),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception: {Method} {Path}", context.Request.Method, context.Request.Path);

            // Handler hatayı yakaladığı için OTel otomatik kaydetmez; Jaeger'da stack trace görünsün.
            var activity = Activity.Current;
            activity?.AddException(exception);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = context.Request.Path
        }, options: null, contentType: "application/problem+json", cancellationToken);

        return true;
    }
}
