using EvolutionaryLogistics.Services.Orchestrator.Contracts;
using EvolutionaryLogistics.Services.Orchestrator.Persistence;
using MassTransit;

namespace EvolutionaryLogistics.Services.Orchestrator.Sagas;

/// <summary>
/// MassTransit State Machine Saga for order fulfillment choreography.
///
/// FLOW:
/// ─────
///   OrderPlaced → [Submitted] → InventoryReserved → [Reserved] → OrderConfirmed → [Completed]
///                              → InventoryReservationFailed → [Faulted]
///                              → (timeout 30s) → [Faulted] (compensate)
///
/// WHY STATE MACHINE SAGA INSTEAD OF ROUTING SLIP?
/// ─────────────────────────────────────────────────
/// - Routing Slip is for sequential, compensatable steps (like a travel booking).
/// - Our flow has branching (reserve success vs. failure) and timeouts.
/// - State Machine Saga gives explicit state transitions with auditable history.
/// - Trade-off: more code, but the state diagram IS the documentation.
///
/// OUTBOX INTEGRATION:
/// ────────────────────
/// MassTransit's EF Core Outbox ensures that saga state transitions and outgoing
/// messages are committed atomically. Without Outbox, a crash between
/// SaveChangesAsync and Publish could leave the saga in an inconsistent state.
/// </summary>
public sealed class OrderFulfillmentSaga : MassTransitStateMachine<OrderSagaState>
{
    // ── States ──
    public State Submitted { get; private set; } = null!;
    public State Reserved { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Faulted { get; private set; } = null!;

    // ── Events ──
    public Event<OrderPlacedEvent> OrderPlaced { get; private set; } = null!;
    public Event<InventoryReservedEvent> InventoryReserved { get; private set; } = null!;
    public Event<InventoryReservationFailedEvent> ReservationFailed { get; private set; } = null!;
    public Event<OrderConfirmedEvent> OrderConfirmed { get; private set; } = null!;

    // ── Timeouts ──
    public Schedule<OrderSagaState, ReservationTimeoutExpired> ReservationTimeout { get; private set; } = null!;

    public OrderFulfillmentSaga()
    {
        // ── Instance correlation ──
        // GOTCHA: CorrelationId MUST be set on every event.
        // Missing correlation = silent message drop in MassTransit.
        // This is the #1 MassTransit debugging headache.
        Event(() => OrderPlaced, x => x.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => InventoryReserved, x => x.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => ReservationFailed, x => x.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => OrderConfirmed, x => x.CorrelateById(ctx => ctx.Message.CorrelationId));

        // ── Timeout schedule ──
        Schedule(() => ReservationTimeout, instance => instance.ReservationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromSeconds(30);
            s.Received = x => x.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        InstanceState(x => x.CurrentState);

        // ════════════════════════════════════════════════════════════════
        //  STATE TRANSITIONS
        // ════════════════════════════════════════════════════════════════

        Initially(
            When(OrderPlaced)
                .Then(ctx =>
                {
                    ctx.Saga.OrderId = ctx.Message.OrderId;
                    ctx.Saga.Sku = ctx.Message.Sku;
                    ctx.Saga.Quantity = ctx.Message.Quantity;
                })
                .Schedule(ReservationTimeout, ctx => new ReservationTimeoutExpired
                {
                    CorrelationId = ctx.Saga.CorrelationId
                })
                .TransitionTo(Submitted)
        );

        During(Submitted,
            When(InventoryReserved)
                .Then(ctx =>
                {
                    ctx.Saga.ReservedAt = ctx.Message.OccurredAt;
                })
                .Unschedule(ReservationTimeout)
                .TransitionTo(Reserved),

            When(ReservationFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                })
                .Unschedule(ReservationTimeout)
                // ── COMPENSATING ACTION ──
                // Publish OrderCancelled so Order Service can revert state.
                .Publish(ctx => new OrderCancelledEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    Reason = $"Inventory reservation failed: {ctx.Message.Reason}",
                    CorrelationId = ctx.Saga.CorrelationId,
                    OccurredAt = DateTimeOffset.UtcNow
                })
                .TransitionTo(Faulted),

            // ── TIMEOUT: Inventory didn't respond ──
            When(ReservationTimeout!.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "Reservation timeout after 30s";
                })
                .Publish(ctx => new OrderCancelledEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    Reason = "Inventory service timeout (30s)",
                    CorrelationId = ctx.Saga.CorrelationId,
                    OccurredAt = DateTimeOffset.UtcNow
                })
                .TransitionTo(Faulted)
        );

        During(Reserved,
            When(OrderConfirmed)
                .Then(ctx =>
                {
                    ctx.Saga.ConfirmedAt = ctx.Message.OccurredAt;
                })
                .Finalize()
        );

        // Cleanup completed sagas from the database
        SetCompletedWhenFinalized();
    }
}

/// <summary>
/// Timeout message — scheduled by the saga, delivered by MassTransit's scheduler.
/// </summary>
public sealed record ReservationTimeoutExpired : CorrelatedBy<Guid>
{
    public required Guid CorrelationId { get; init; }
}
