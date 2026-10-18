using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EvolutionaryLogistics.BuildingBlocks.Observability;

/// <summary>
/// Tek merkezden yönetilen ActivitySource + Meter isimleri.
///
/// NEDEN STATIC ve TEK YERDE?
/// ───────────────────────────
/// 1) <see cref="ActivitySource"/> ve <see cref="Meter"/> <b>static</b> tutulmalı ve
///    <b>Dispose edilmemelidir</b>. Listener (OTel SDK) eklenmiş olsa da olmasa da
///    aynı örnek yaşamalıdır.
/// 2) OTel SDK'ya <c>.AddSource("EvolutionaryLogistics.Telemetry")</c> ile <b>tam eşleşme</b>
///    kaydedilmezse span'ler sessizce üretilmez (hata vermez, sadece görünmez).
///
/// METRIC CARDINALITY KURALI
/// ─────────────────────────
/// Label'lar <b>bounded</b> olmalıdır: model adı, yön (prompt/completion), outcome.
/// SKU, tenant-id, order-id, kullanıcı-id ASLA label olmaz — her yeni değer yeni bir
/// time-series yaratır ve Prometheus'u şişirir (Lab 10 kardinalite bombası).
/// </summary>
public static class ObservabilityNames
{
    public const string ServiceNamespace = "evolutionary-logistics";

    // ── Tracers ──
    public static readonly ActivitySource TelemetryPipeline = new("EvolutionaryLogistics.Telemetry");
    public static readonly ActivitySource AiPipeline = new("EvolutionaryLogistics.Ai");

    // ── Meter (tek process-tek meter) ──
    public static readonly Meter TelemetryMeter = new("EvolutionaryLogistics.Telemetry");

    // ── Telemetry pipeline metrikleri ──
    public static readonly Histogram<double> BatchFlushMs =
        TelemetryMeter.CreateHistogram<double>("telemetry.batch_flush_ms");

    public static readonly Counter<long> PointsIngested =
        TelemetryMeter.CreateCounter<long>("telemetry.points_ingested");

    // ── AI / LLM metrikleri (Gün 4) ──
    public static readonly Counter<long> LlmTokens =
        TelemetryMeter.CreateCounter<long>("llm.tokens", unit: "tokens");

    public static readonly Histogram<double> RetrievalTopScore =
        TelemetryMeter.CreateHistogram<double>("rag.top_score");

    public static readonly Counter<long> ToolInvocations =
        TelemetryMeter.CreateCounter<long>("ai.tool.calls");
}
