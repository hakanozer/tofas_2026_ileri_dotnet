using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace EvolutionaryLogistics.Services.Orchestrator.Persistence;

/// <summary>
/// EF Core 10 DbContext for the Outbox pattern.
///
/// DESIGN DECISIONS:
/// ─────────────────
/// 1) MassTransit InboxState/OutboxState/OutboxMessage entities are mapped here.
///    MassTransit's EF Core outbox uses these tables to:
///    - Store messages in the same transaction as business state changes (OutboxMessage)
///    - Track delivered messages for deduplication (InboxState)
///    - Track delivery state per destination (OutboxState)
///
/// 2) Schema isolation: all outbox tables live in the "outbox" schema.
///    This prevents collision with domain tables and makes it clear
///    which tables are infrastructure vs. domain.
///
/// 3) UseSnakeCaseNamingConvention: PostgreSQL convention.
///    EF Core default is PascalCase which is quoted in PG — operational nightmare.
/// </summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Domain entity: tracks the state of each order through the saga.
    /// This lives alongside outbox tables so that saga state changes
    /// and message publishing happen in a SINGLE transaction.
    /// </summary>
    public DbSet<OrderSagaState> OrderSagas => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("outbox");

        // ── MassTransit Outbox Tables ──
        // These three entity types are required for MassTransit's EF Core Outbox.
        // They store pending messages, delivery state, and inbox deduplication.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();

        // ── Saga State ──
        modelBuilder.Entity<OrderSagaState>(entity =>
        {
            entity.ToTable("order_saga_states");
            entity.HasKey(s => s.CorrelationId);

            entity.Property(s => s.OrderId).IsRequired();
            entity.Property(s => s.Sku).HasMaxLength(64).IsRequired();
            entity.Property(s => s.Quantity).IsRequired();
            entity.Property(s => s.CurrentState).HasMaxLength(64).IsRequired();

            entity.HasIndex(s => s.OrderId);
            entity.HasIndex(s => s.CurrentState);

            // ── GOTCHA: Concurrency Token ──
            // Without this, two saga instances processing the same CorrelationId
            // could overwrite each other's state. PostgreSQL's system column xmin
            // provides optimistic concurrency without an explicit rowversion column.
            // NOTE: Npgsql EF Core 10 removed the UseXminAsConcurrencyToken() sugar,
            // so we map the shadow property explicitly (equivalent behavior).
            entity.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }
}

/// <summary>
/// Saga state entity — tracks order lifecycle through choreography.
/// MassTransit maps this via <see cref="OrderFulfillmentSaga"/>.
/// </summary>
public sealed class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public required string CurrentState { get; set; }
    public required Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public required int Quantity { get; set; }
    public DateTimeOffset? ReservedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>
    /// MassTransit schedule token for the reservation timeout.
    /// MassTransit stores the scheduled message token here so it can unschedule
    /// the timeout when the saga leaves the Submitted state.
    /// </summary>
    public Guid? ReservationTimeoutTokenId { get; set; }
}
