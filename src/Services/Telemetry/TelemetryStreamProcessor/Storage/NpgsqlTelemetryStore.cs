using System.Diagnostics;
using EvolutionaryLogistics.Services.Telemetry.Models;
using EvolutionaryLogistics.Services.Telemetry.Processing;
using Npgsql;

namespace EvolutionaryLogistics.Services.Telemetry.Storage;

/// <summary>
/// High-throughput telemetry storage using Npgsql COPY for bulk inserts.
///
/// WHY COPY INSTEAD OF INSERT?
/// ────────────────────────────
/// - INSERT with VALUES: ~5k rows/sec (round-trip per statement)
/// - INSERT with UNNEST: ~30k rows/sec (single round-trip, but query parsing overhead)
/// - COPY BINARY:        ~100k+ rows/sec (binary protocol, no SQL parsing, minimal overhead)
///
/// IDEMPOTENCY:
/// We use a UNIQUE constraint on (device_id, timestamp_unix_ms).
/// COPY doesn't support ON CONFLICT, so we use a temp table + INSERT ... ON CONFLICT.
/// This two-step approach preserves COPY throughput while ensuring idempotency.
/// </summary>
public sealed class NpgsqlTelemetryStore(IConfiguration configuration, ILogger<NpgsqlTelemetryStore> logger) : ITelemetryStore
{
    private static readonly ActivitySource ActivitySource = new("Telemetry.Storage");

    private readonly string _connectionString = configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Postgres connection string is missing");

    public async Task BulkUpsertAsync(ReadOnlyMemory<TelemetryPayload> items, CancellationToken ct)
    {
        if (items.Length == 0) return;

        using var activity = ActivitySource.StartActivity("BulkUpsertTelemetry");
        activity?.SetTag("batch.size", items.Length);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Step 1: COPY into temp table (blazing fast, binary protocol)
        await using var cmd = new NpgsqlCommand("""
            CREATE TEMP TABLE IF NOT EXISTS _telemetry_staging (
                device_id     uuid NOT NULL,
                timestamp_ms  bigint NOT NULL,
                latitude      double precision NOT NULL,
                longitude     double precision NOT NULL,
                speed_kmh     real NOT NULL,
                heading       real NOT NULL,
                fuel_level    real NOT NULL,
                engine_temp   real NOT NULL
            ) ON COMMIT DROP
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY _telemetry_staging (device_id, timestamp_ms, latitude, longitude, speed_kmh, heading, fuel_level, engine_temp) FROM STDIN (FORMAT BINARY)",
            ct);

        for (var i = 0; i < items.Length; i++)
        {
            var p = items.Span[i];
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(p.DeviceId, ct);
            await writer.WriteAsync(p.TimestampUnixMs, ct);
            await writer.WriteAsync(p.Latitude, ct);
            await writer.WriteAsync(p.Longitude, ct);
            await writer.WriteAsync(p.SpeedKmh, ct);
            await writer.WriteAsync(p.Heading, ct);
            await writer.WriteAsync(p.FuelLevel, ct);
            await writer.WriteAsync(p.EngineTemp, ct);
        }

        await writer.CompleteAsync(ct);

        // Step 2: Upsert from staging → target with ON CONFLICT DO NOTHING
        await using var upsert = new NpgsqlCommand("""
            INSERT INTO telemetry.readings (device_id, timestamp_ms, latitude, longitude, speed_kmh, heading, fuel_level, engine_temp)
            SELECT device_id, timestamp_ms, latitude, longitude, speed_kmh, heading, fuel_level, engine_temp
            FROM _telemetry_staging
            ON CONFLICT (device_id, timestamp_ms) DO NOTHING
            """, conn);

        var inserted = await upsert.ExecuteNonQueryAsync(ct);
        activity?.SetTag("rows.inserted", inserted);
        logger.LogDebug("Bulk upsert: {Inserted}/{Total} rows", inserted, items.Length);
    }
}
