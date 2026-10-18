namespace EvolutionaryLogistics.Services.Telemetry.Kafka;

/// <summary>
/// Configuration POCO bound from appsettings "Kafka" section.
/// </summary>
public sealed class KafkaConsumerOptions
{
    public const string SectionName = "Kafka";

    public required string BootstrapServers { get; init; }
    public required string GroupId { get; init; }
    public required string Topic { get; init; }

    /// <summary>
    /// Max messages per flush batch. Higher = better throughput, worse latency.
    /// Production default: 500–2000 depending on message size.
    /// </summary>
    public int BatchSize { get; init; } = 500;

    /// <summary>
    /// Force flush even if batch is not full after this interval.
    /// Prevents high tail latency under low traffic.
    /// </summary>
    public int FlushIntervalMs { get; init; } = 1000;

    /// <summary>
    /// Bounded channel capacity. Controls how many batches can queue
    /// before back-pressure pauses the Kafka poll loop.
    /// </summary>
    public int ChannelCapacity { get; init; } = 32;

    /// <summary>
    /// Number of parallel processor tasks reading from the channel.
    /// Typically = number of CPU cores / 2 for I/O-bound work.
    /// </summary>
    public int ProcessorConcurrency { get; init; } = 4;
}
