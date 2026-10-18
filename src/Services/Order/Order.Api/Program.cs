using EvolutionaryLogistics.BuildingBlocks.Observability;

var builder = WebApplication.CreateBuilder(args);

// ── Gün 3: Observability + structured logging ──
builder.AddEvolutionarySerilog("order-service");
builder.AddEvolutionarySentry("order-service", "order-service@1.0.0");
builder.Services.AddEvolutionaryObservability("order-service");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", context = "orders" }));
app.MapGet("/", () => Results.Ok(new { message = "Order service — Day 1 ES/CQRS labs land here" }));

app.Run();
