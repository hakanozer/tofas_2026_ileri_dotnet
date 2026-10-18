using EvolutionaryLogistics.BuildingBlocks.Grpc.DependencyInjection;
using EvolutionaryLogistics.BuildingBlocks.Observability;

var builder = WebApplication.CreateBuilder(args);

// ── Gün 3: Observability + structured logging ──
builder.AddEvolutionarySerilog("inventory-service");
builder.AddEvolutionarySentry("inventory-service", "inventory-service@1.4.2");
builder.Services.AddEvolutionaryObservability("inventory-service");

builder.Services.AddEvolutionaryGrpcServerPipeline();
builder.Services.AddEvolutionaryGrpcClientPipeline();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", context = "inventory" }));

// Day 2: MapGrpcService<InventoryGrpcService>() after proto + interceptor validation lab
app.Run();
