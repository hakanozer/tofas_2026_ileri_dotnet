namespace EvolutionaryLogistics.BuildingBlocks.Kernel.Grpc;

/// <summary>
/// Canonical metadata keys — keep in sync across REST gateway, gRPC server, and Kafka headers (Day 2).
/// </summary>
public static class GrpcMetadataKeys
{
    public const string Authorization = "authorization";
    public const string TenantId = "x-tenant-id";
    public const string UserId = "x-user-id";
    public const string UserRoles = "x-user-roles";
    public const string CorrelationId = "x-correlation-id";
    public const string DeadlineUtc = "x-deadline-utc";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
}

public sealed class GrpcCallContext
{
    public required string CorrelationId { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTimeOffset? DeadlineUtc { get; init; }
}
