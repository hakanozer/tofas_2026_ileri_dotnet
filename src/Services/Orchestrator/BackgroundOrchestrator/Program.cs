using EvolutionaryLogistics.BuildingBlocks.Observability;
using EvolutionaryLogistics.Services.Orchestrator.Persistence;
using EvolutionaryLogistics.Services.Orchestrator.Sagas;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// ── Gün 3: Observability + structured logging ──
builder.AddEvolutionarySerilog("background-orchestrator");
builder.AddEvolutionarySentry("background-orchestrator", "background-orchestrator@1.0.0");
builder.Services.AddEvolutionaryObservability("background-orchestrator", includeAspNetCoreInstrumentation: false);

// ── PostgreSQL + EF Core 10 ──
builder.Services.AddDbContext<OutboxDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npgsql =>
    {
        npgsql.MigrationsHistoryTable("__ef_migrations", "outbox");

        // ── GOTCHA: EnableRetryOnFailure ──
        // EF Core retry strategy conflicts with MassTransit's outbox retry.
        // When the outbox delivery fails, EF retries the SaveChanges,
        // but MassTransit also retries the consumer. This causes duplicate messages.
        // Solution: disable EF retry and let MassTransit handle retries.
        // npgsql.EnableRetryOnFailure(3); // DISABLED — see above
    });
});

// ── MassTransit + RabbitMQ + EF Core Outbox ──
builder.Services.AddMassTransit(x =>
{
    // Register saga state machine with EF Core repository
    x.AddSagaStateMachine<OrderFulfillmentSaga, OrderSagaState>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<OutboxDbContext>();
            r.UsePostgres();

            // ── Pessimistic lock for saga state ──
            // In high-throughput scenarios, optimistic concurrency (xmin)
            // causes excessive retries. FOR UPDATE SKIP LOCKED gives us
            // partition-level concurrency without contention.
            r.LockStatementProvider = new PostgresLockStatementProvider();
        });

    // ── Outbox Configuration (MassTransit v8 bus-level registration) ──
    // Messages are stored in PostgreSQL in the SAME transaction as saga state
    // changes. A background thread polls the outbox and delivers to RabbitMQ.
    //
    // GOTCHA: DuplicateDetectionWindow — MassTransit uses InboxState to dedupe.
    // Window must be > max message processing time. Too short = duplicates slip
    // through. Too long = inbox table bloats (needs periodic cleanup).
    x.AddEntityFrameworkOutbox<OutboxDbContext>(o =>
    {
        o.QueryDelay = TimeSpan.FromSeconds(1);
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(5);
        o.UsePostgres();   // FOR UPDATE SKIP LOCKED for outbox polling
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "logistics");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "logistics_secret");
        });

        // ── Consumer retry policy ──
        // Exponential backoff: 1s, 2s, 4s (3 retries).
        // After that, message goes to _error queue for manual inspection.
        cfg.UseMessageRetry(r => r.Exponential(3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(1)));

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHostedService<OrchestratorWorker>();

var host = builder.Build();

// ── Auto-migrate on startup (DEV only) ──
// In production, use explicit migration scripts.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();

/// <summary>
/// Minimal hosted service — MassTransit handles its own bus lifecycle.
/// This exists only to log startup confirmation and keep the host alive.
/// </summary>
public sealed class OrchestratorWorker(ILogger<OrchestratorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background Orchestrator started — MassTransit bus is managing sagas & outbox delivery");

        // MassTransit bus runs its own background threads.
        // We just keep this service alive.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
