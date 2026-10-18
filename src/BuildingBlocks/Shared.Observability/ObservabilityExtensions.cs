using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EvolutionaryLogistics.BuildingBlocks.Observability;

/// <summary>
/// Ortak OpenTelemetry bootstrap'i. Her servis kendi Program.cs'inde tek satır çağırır:
///
/// <code>builder.Services.AddEvolutionaryObservability("inventory-service");</code>
///
/// OTLP endpoint'i <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> ortam değişkeninden okunur
/// (docker-compose <c>x-dotnet-env</c> anchor'ı: http://otel-collector:4317).
/// Yerel geliştirmede de aynı env var ile override edilebilir.
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddEvolutionaryObservability(
        this IServiceCollection services,
        string serviceName,
        bool includeAspNetCoreInstrumentation = true)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: ObservabilityNames.ServiceNamespace)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] =
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
                }))
            .WithTracing(tracing =>
            {
                // ── Head sampling ──
                // Oran %10; ParentBased → upstream (gateway) kararı korunur, aksi halde
                // dağıtık trace'ler zincirde kopar. Hatalı/yavaş trace'ler için
                // collector'daki tail_sampling devreye girer (Lab 6).
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.10)))
                    .AddSource(ObservabilityNames.TelemetryPipeline.Name)
                    .AddSource(ObservabilityNames.AiPipeline.Name)
                    // Gün 2'den kalan elle tanımlanmış source'lar (kaldırılınca span kaybolur!)
                    .AddSource("Telemetry.KafkaConsumer")
                    .AddSource("Telemetry.BatchProcessor")
                    .AddSource("Telemetry.Storage")
                    .AddSource("BuildingBlocks.Caching")
                    .AddSource("BuildingBlocks.CacheInvalidation")
                    .AddHttpClientInstrumentation();

                if (includeAspNetCoreInstrumentation)
                {
                    tracing.AddAspNetCoreInstrumentation();
                }

                tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ObservabilityNames.TelemetryMeter.Name)
                    // .NET runtime / GC metrikleri → GC pause paneli (Lab 1)
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation();

                if (includeAspNetCoreInstrumentation)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                metrics.AddOtlpExporter();
            });

        return services;
    }
}
