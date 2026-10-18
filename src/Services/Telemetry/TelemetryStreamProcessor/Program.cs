using EvolutionaryLogistics.BuildingBlocks.Observability;
using EvolutionaryLogistics.Services.Telemetry;
using EvolutionaryLogistics.Services.Telemetry.Kafka;
using EvolutionaryLogistics.Services.Telemetry.Processing;
using EvolutionaryLogistics.Services.Telemetry.Storage;

var builder = Host.CreateApplicationBuilder(args);

// ── Gün 3: Observability + structured logging tek satırda ──
// AddSource("EvolutionaryLogistics.Telemetry") burada bağlanır; yoksa span ÜRETİLMEZ.
builder.AddEvolutionarySerilog("telemetry-stream-processor");
builder.AddEvolutionarySentry("telemetry-stream-processor", "telemetry-stream-processor@1.0.0");
builder.Services.AddEvolutionaryObservability("telemetry-stream-processor", includeAspNetCoreInstrumentation: false);

// ── Kafka Configuration ──
var kafkaOptions = builder.Configuration
    .GetSection(KafkaConsumerOptions.SectionName)
    .Get<KafkaConsumerOptions>()
    ?? throw new InvalidOperationException("Kafka configuration section is missing");

builder.Services.AddSingleton(kafkaOptions);
builder.Services.AddSingleton<KafkaTelemetryConsumer>();
builder.Services.AddSingleton<TelemetryBatchProcessor>();
builder.Services.AddSingleton<ITelemetryStore, NpgsqlTelemetryStore>();

builder.Services.AddHostedService<TelemetryWorker>();

var host = builder.Build();
host.Run();
