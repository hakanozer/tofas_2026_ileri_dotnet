using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvolutionaryLogistics.Services.Telemetry.Models;

/// <summary>
/// GPS/Sensor telemetry data point — kept as struct to avoid GC pressure.
/// Layout: sequential for potential SIMD/bulk copy in future hot-path.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct TelemetryPayload
{
    [JsonPropertyName("device_id")]
    public required Guid DeviceId { get; init; }

    [JsonPropertyName("ts")]
    public required long TimestampUnixMs { get; init; }

    [JsonPropertyName("lat")]
    public required double Latitude { get; init; }

    [JsonPropertyName("lng")]
    public required double Longitude { get; init; }

    [JsonPropertyName("speed")]
    public required float SpeedKmh { get; init; }

    [JsonPropertyName("heading")]
    public required float Heading { get; init; }

    [JsonPropertyName("fuel")]
    public required float FuelLevel { get; init; }

    [JsonPropertyName("temp")]
    public required float EngineTemp { get; init; }

    /// <summary>
    /// Zero-allocation deserialization from a rented UTF-8 byte buffer.
    /// Caller owns the buffer lifetime — no copies made here.
    /// </summary>
    public static TelemetryPayload DeserializeFromUtf8(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        return JsonSerializer.Deserialize<TelemetryPayload>(ref reader);
    }
}
