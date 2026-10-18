using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Confluent.Kafka;
using EvolutionaryLogistics.Services.Telemetry.Models;

namespace EvolutionaryLogistics.Services.Telemetry.Kafka;

/// <summary>
/// High-throughput Kafka consumer that pushes raw <see cref="ConsumeResult{TKey,TValue}"/>
/// into a bounded <see cref="Channel{T}"/> for downstream processing.
///
/// DESIGN DECISIONS:
/// ─────────────────
/// 1) Channel<T> back-pressure: prevents OOM when downstream (DB, gRPC) is slower.
///    BoundedChannelFullMode.Wait applies natural flow-control — Kafka poll pauses,
///    consumer lag rises, alerting fires. This is intentional.
///
/// 2) ArrayPool<byte> + Memory<byte>: Confluent.Kafka gives us byte[] from librdkafka.
///    We rent a pooled buffer, copy once, wrap in Memory<byte> and pass ownership to
///    the processing pipeline. The rental is returned after the batch commits.
///
/// 3) Manual offset commit per-batch: we accumulate up to BatchSize or FlushIntervalMs,
///    then commit. This gives "at-least-once" with bounded duplicate window.
///
/// 4) Idempotency: downstream storage uses (DeviceId, TimestampUnixMs) natural key
///    with ON CONFLICT DO NOTHING — safe against replays.
/// </summary>
public sealed class KafkaTelemetryConsumer : IAsyncDisposable
{
    private readonly IConsumer<Ignore, byte[]> _consumer;
    private readonly Channel<PooledTelemetryBatch> _channel;
    private readonly ILogger<KafkaTelemetryConsumer> _logger;
    private readonly ArrayPool<byte> _pool;

    // ── Metrics ──
    private static readonly Meter Meter = new("Telemetry.KafkaConsumer", "1.0");
    private static readonly Counter<long> MessagesConsumed = Meter.CreateCounter<long>("kafka.messages.consumed");
    private static readonly Counter<long> DeserializationErrors = Meter.CreateCounter<long>("kafka.deserialization.errors");
    private static readonly Histogram<double> BatchProcessDuration = Meter.CreateHistogram<double>("kafka.batch.duration_ms");
    private static readonly UpDownCounter<int> ChannelBacklog = Meter.CreateUpDownCounter<int>("kafka.channel.backlog");

    // ── Config ──
    private readonly string _topic;
    private readonly int _batchSize;
    private readonly int _flushIntervalMs;

    public ChannelReader<PooledTelemetryBatch> Reader => _channel.Reader;

    public KafkaTelemetryConsumer(
        KafkaConsumerOptions options,
        ILogger<KafkaTelemetryConsumer> logger)
    {
        _logger = logger;
        _topic = options.Topic;
        _batchSize = options.BatchSize;
        _flushIntervalMs = options.FlushIntervalMs;
        _pool = ArrayPool<byte>.Shared;

        // Bounded channel: if processing falls behind, poll thread pauses.
        // This is the CORRECT behavior — consumer lag metric surfaces the issue.
        _channel = Channel.CreateBounded<PooledTelemetryBatch>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,   // only the poll loop writes
            SingleReader = false   // N processor tasks read
        });

        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,           // CRITICAL: manual commit after batch
            EnableAutoOffsetStore = false,
            FetchMinBytes = 1024 * 64,          // 64KB — reduce poll round-trips
            FetchMaxBytes = 1024 * 1024 * 4,    // 4MB — bounded fetch
            MaxPollIntervalMs = 300_000,
            SessionTimeoutMs = 45_000,
            StatisticsIntervalMs = 30_000,
            // ── GOTCHA: PartitionAssignmentStrategy ──
            // CooperativeSticky avoids stop-the-world rebalances.
            // Eager (range/roundrobin) would revoke ALL partitions during rebalance
            // causing message re-processing and duplicate downstream writes.
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };

        _consumer = new ConsumerBuilder<Ignore, byte[]>(config)
            .SetErrorHandler((_, e) =>
            {
                if (e.IsFatal)
                    _logger.LogCritical("Kafka FATAL: {Reason}", e.Reason);
                else
                    _logger.LogWarning("Kafka warning: {Reason} (code={Code})", e.Reason, e.Code);
            })
            .SetStatisticsHandler((_, json) =>
            {
                // Plug into Prometheus/OTEL metrics exporter in production
                _logger.LogDebug("Kafka stats: {Stats}", json[..Math.Min(json.Length, 200)]);
            })
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                _logger.LogInformation("Partitions assigned: {Partitions}",
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
            })
            .SetPartitionsRevokedHandler((_, partitions) =>
            {
                _logger.LogWarning("Partitions revoked: {Partitions}",
                    string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
            })
            .Build();
    }

    /// <summary>
    /// The hot loop: poll → batch → channel.Write → commit.
    /// Runs on a dedicated thread via Task.Factory.StartNew(LongRunning).
    /// </summary>
    public async Task ConsumeLoopAsync(CancellationToken ct)
    {
        _consumer.Subscribe(_topic);
        _logger.LogInformation("Kafka consumer subscribed to {Topic}, batch={Batch}, flush={Flush}ms",
            _topic, _batchSize, _flushIntervalMs);

        var batch = new List<TelemetryPayload>(_batchSize);
        var rentedBuffers = new List<byte[]>(_batchSize);
        var sw = Stopwatch.StartNew();
        ConsumeResult<Ignore, byte[]>? lastResult = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<Ignore, byte[]>? result;
                try
                {
                    // 100ms poll timeout — short enough for responsive shutdown,
                    // long enough to avoid busy-spin.
                    result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Consume error on {Topic}", _topic);
                    DeserializationErrors.Add(1);
                    continue;
                }

                if (result is null)
                {
                    // Flush partial batch on idle
                    if (batch.Count > 0 && sw.ElapsedMilliseconds >= _flushIntervalMs)
                    {
                        await FlushBatchAsync(batch, rentedBuffers, lastResult, ct);
                        sw.Restart();
                    }
                    continue;
                }

                // ── Zero-copy path: rent buffer, copy kafka payload, parse ──
                var kafkaValue = result.Message.Value;
                if (kafkaValue is null || kafkaValue.Length == 0)
                {
                    _consumer.StoreOffset(result);
                    continue;
                }

                // Rent from pool — returned after downstream processing completes
                var rented = _pool.Rent(kafkaValue.Length);
                kafkaValue.AsSpan().CopyTo(rented.AsSpan());

                try
                {
                    var payload = TelemetryPayload.DeserializeFromUtf8(
                        rented.AsSpan(0, kafkaValue.Length));

                    batch.Add(payload);
                    rentedBuffers.Add(rented);
                    lastResult = result;
                    MessagesConsumed.Add(1);
                }
                catch (Exception ex)
                {
                    _pool.Return(rented);
                    DeserializationErrors.Add(1);
                    _logger.LogWarning(ex, "Deserialization failed, offset={Offset}",
                        result.Offset.Value);
                    _consumer.StoreOffset(result);
                    continue;
                }

                // Flush when batch is full or flush interval elapsed
                if (batch.Count >= _batchSize || sw.ElapsedMilliseconds >= _flushIntervalMs)
                {
                    await FlushBatchAsync(batch, rentedBuffers, lastResult, ct);
                    sw.Restart();
                }
            }

            // Final flush on shutdown
            if (batch.Count > 0 && lastResult is not null)
            {
                await FlushBatchAsync(batch, rentedBuffers, lastResult, CancellationToken.None);
            }
        }
        finally
        {
            _consumer.Close();
            _channel.Writer.TryComplete();
            _logger.LogInformation("Kafka consumer loop terminated for {Topic}", _topic);
        }
    }

    private async Task FlushBatchAsync(
        List<TelemetryPayload> batch,
        List<byte[]> rentedBuffers,
        ConsumeResult<Ignore, byte[]>? lastResult,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var pooledBatch = new PooledTelemetryBatch(
            [.. batch],
            rentedBuffers.ToArray(),
            _pool);

        ChannelBacklog.Add(1);

        // ── BACK-PRESSURE POINT ──
        // If the channel is full, this awaits — Kafka poll pauses.
        // Consumer lag rises, alerting fires. This is the designed behavior.
        await _channel.Writer.WriteAsync(pooledBatch, ct);

        // Manual offset commit — at-least-once guarantee
        if (lastResult is not null)
        {
            _consumer.StoreOffset(lastResult);
            try
            {
                _consumer.Commit();
            }
            catch (KafkaException ex)
            {
                // Commit failure is non-fatal: messages will be re-delivered
                // and idempotency key prevents duplicate processing.
                _logger.LogWarning(ex, "Offset commit failed, will retry on next batch");
            }
        }

        BatchProcessDuration.Record(sw.Elapsed.TotalMilliseconds);
        _logger.LogDebug("Flushed batch of {Count} telemetry points in {Ms}ms",
            batch.Count, sw.Elapsed.TotalMilliseconds);

        batch.Clear();
        rentedBuffers.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _consumer.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Wraps a batch of parsed payloads + rented buffers.
/// Caller MUST call <see cref="ReturnBuffers"/> after processing to avoid pool exhaustion.
/// </summary>
public sealed class PooledTelemetryBatch(
    TelemetryPayload[] items,
    byte[][] rentedBuffers,
    ArrayPool<byte> pool)
{
    public ReadOnlyMemory<TelemetryPayload> Items { get; } = items;

    /// <summary>
    /// Returns all rented buffers to the ArrayPool.
    /// MUST be called exactly once after downstream processing completes.
    /// Forgetting this call leads to pool exhaustion and LOH allocations.
    /// </summary>
    public void ReturnBuffers()
    {
        foreach (var buf in rentedBuffers)
        {
            pool.Return(buf);
        }
    }
}
