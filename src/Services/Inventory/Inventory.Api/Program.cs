using EvolutionaryLogistics.BuildingBlocks.Auth;
using EvolutionaryLogistics.BuildingBlocks.Grpc.DependencyInjection;
using EvolutionaryLogistics.BuildingBlocks.Observability;
using EvolutionaryLogistics.Services.Inventory.Data;
using EvolutionaryLogistics.Services.Inventory.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Gün 3: Observability + structured logging ──
builder.AddEvolutionarySerilog("inventory-service");
builder.AddEvolutionarySentry("inventory-service", "inventory-service@1.4.2");
builder.Services.AddEvolutionaryObservability("inventory-service");

builder.Services.AddEvolutionaryGrpcServerPipeline();
builder.Services.AddEvolutionaryGrpcClientPipeline();

// ── Stok yönetimi + rezervasyon: kendi veritabanı (inventory_db), JWT (rol: inventory), servisler arası API key ──
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")
        ?? throw new InvalidOperationException("ConnectionStrings:InventoryDb is not configured.")));
builder.Services.AddScoped<IReservationService, ReservationService>();

builder.Services.AddServiceJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(InventoryEndpoints.InventoryPolicy, p => p.RequireAuthenticatedUser().RequireRole("inventory"));

builder.Services.Configure<InternalApiOptions>(builder.Configuration.GetSection(InternalApiOptions.Section));
if (string.IsNullOrEmpty(builder.Configuration[$"{InternalApiOptions.Section}:ApiKey"]))
    throw new InvalidOperationException("Internal:ApiKey must be configured (env Internal__ApiKey).");

builder.Services.AddProblemDetails();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    if (db.Database.IsNpgsql()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();

    if (app.Environment.IsDevelopment()) await InventorySeed.SeedAsync(db);
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", context = "inventory" }));
app.MapInventoryEndpoints();

// Day 2: MapGrpcService<InventoryGrpcService>() after proto + interceptor validation lab
app.Run();

public partial class Program;
