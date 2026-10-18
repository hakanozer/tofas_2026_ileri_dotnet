using EvolutionaryLogistics.Services.Telemetry.Kafka;
using EvolutionaryLogistics.Services.Telemetry.Processing;

namespace EvolutionaryLogistics.Services.Telemetry;

/// <summary>
/// Hosted service that manages the Kafka consume loop + N parallel processor tasks.
///
/// LIFECYCLE:
/// ──────────
/// 1) StartAsync: Creates the consumer, launches the poll loop on a dedicated thread,
///    and spawns ProcessorConcurrency processor tasks.
/// 2) StopAsync: Cancels the token → poll loop exits → channel completes →
///    processors drain and exit → buffers returned.
///
/// WHY Task.Factory.StartNew(LongRunning)?
/// ─────────────────────────────────────────
/// The Kafka poll loop calls Consume() which blocks the thread (librdkafka C interop).
/// Using a ThreadPool thread would starve the pool. LongRunning creates a dedicated thread.
/// </summary>
public sealed class TelemetryWorker(
    KafkaTelemetryConsumer consumer,
    TelemetryBatchProcessor processor,
    KafkaConsumerOptions options,
    ILogger<TelemetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "TelemetryWorker starting: topic={Topic}, batch={Batch}, concurrency={Concurrency}",
            options.Topic, options.BatchSize, options.ProcessorConcurrency);

        // ── GOTCHA: Task.Factory.StartNew vs Task.Run ──
        // Task.Run uses ThreadPool (bounded). The Kafka poll loop blocks its thread
        // for the entire lifetime of the service. LongRunning hints the scheduler
        // to create a new thread. Without this, under high load the ThreadPool
        // starves and ASP.NET request handling degrades.
        var consumeTask = Task.Factory.StartNew(
            () => consumer.ConsumeLoopAsync(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        // Spawn N parallel processor tasks
        var processorTasks = Enumerable
            .Range(0, options.ProcessorConcurrency)
            .Select(_ => Task.Run(() => processor.ProcessLoopAsync(consumer, stoppingToken), stoppingToken))
            .ToArray();

        // Wait for all tasks; if any faults, the others will be cancelled via stoppingToken
        try
        {
            await Task.WhenAll([consumeTask, .. processorTasks]);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("TelemetryWorker stopped gracefully");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "TelemetryWorker faulted");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("TelemetryWorker shutting down, draining channel...");
        await base.StopAsync(cancellationToken);
        await consumer.DisposeAsync();
    }
}
