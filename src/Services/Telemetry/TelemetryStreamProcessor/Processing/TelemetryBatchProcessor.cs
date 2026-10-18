using System.Diagnostics;
using EvolutionaryLogistics.Services.Telemetry.Kafka;
using EvolutionaryLogistics.Services.Telemetry.Models;

namespace EvolutionaryLogistics.Services.Telemetry.Processing;

/// <summary>
/// Reads <see cref="PooledTelemetryBatch"/> from the channel and persists to storage.
///
/// ARCHITECTURE NOTES:
/// ───────────────────
/// - Multiple instances run in parallel (ProcessorConcurrency).
/// - Each batch is independent — no cross-batch ordering required for telemetry.
/// - After processing, MUST call <c>batch.ReturnBuffers()</c> to avoid ArrayPool exhaustion.
/// - Uses <see cref="Activity"/> for OTEL tracing to link Kafka consume → DB write spans.
/// </summary>
public sealed class TelemetryBatchProcessor(
    ITelemetryStore store,
    ILogger<TelemetryBatchProcessor> logger)
{
    private static readonly ActivitySource ActivitySource = new("Telemetry.BatchProcessor");

    public async Task ProcessLoopAsync(KafkaTelemetryConsumer consumer, CancellationToken ct)
    {
        logger.LogInformation("Telemetry batch processor started");

        await foreach (var batch in consumer.Reader.ReadAllAsync(ct))
        {
            using var activity = ActivitySource.StartActivity("ProcessTelemetryBatch");
            activity?.SetTag("batch.size", batch.Items.Length);

            try
            {
                // Bulk upsert with ON CONFLICT DO NOTHING for idempotency
                await store.BulkUpsertAsync(batch.Items, ct);

                activity?.SetTag("batch.status", "success");
                logger.LogDebug("Processed batch of {Count} telemetry points", batch.Items.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Failed to process telemetry batch of {Count}", batch.Items.Length);

                // ── GOTCHA: Don't rethrow ──
                // Rethrowing kills the processor task. The batch is lost.
                // In production, send to a Dead Letter Topic (DLT) instead.
                // For training, we log and continue.
            }
            finally
            {
                // ── CRITICAL: Always return rented buffers ──
                // Failing to return causes ArrayPool to allocate new arrays,
                // eventually hitting LOH and causing Gen2 GC storms.
                batch.ReturnBuffers();
            }
        }

        logger.LogInformation("Telemetry batch processor completed");
    }
}

/// <summary>
/// Storage abstraction for telemetry data.
/// Implementation uses Npgsql COPY for maximum throughput.
/// </summary>
public interface ITelemetryStore
{
    /// <summary>
    /// Bulk upsert telemetry data. Uses (DeviceId, TimestampUnixMs) as natural key
    /// with ON CONFLICT DO NOTHING for idempotent processing.
    /// </summary>
    Task BulkUpsertAsync(ReadOnlyMemory<TelemetryPayload> items, CancellationToken ct);
}
