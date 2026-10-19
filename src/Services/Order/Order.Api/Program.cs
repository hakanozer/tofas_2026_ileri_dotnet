using EvolutionaryLogistics.BuildingBlocks.Auth;
using EvolutionaryLogistics.BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using Order.Api.Caching;
using Order.Api.Clients;
using Order.Api.Data;
using Order.Api.Endpoints;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Gün 3: Observability + structured logging ──
builder.AddEvolutionarySerilog("order-service");
builder.AddEvolutionarySentry("order-service", "order-service@1.0.0");
builder.Services.AddEvolutionaryObservability("order-service");

// ── Sipariş API'si: kendi veritabanı (order_db), JWT (rol: order), Redis liste cache'i, Inventory'e HTTP ──
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")
        ?? throw new InvalidOperationException("ConnectionStrings:OrderDb is not configured.")));

builder.Services.AddServiceJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(OrderEndpoints.OrdersPolicy, p => p.RequireAuthenticatedUser().RequireRole("order"));

builder.Services.Configure<OrderCacheOptions>(builder.Configuration.GetSection(OrderCacheOptions.Section));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
    builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false"));
builder.Services.AddSingleton<IOrderListCache, RedisOrderListCache>();

var inventoryBaseUrl = builder.Configuration[$"{InventoryClientOptions.Section}:BaseUrl"];
var internalApiKey = builder.Configuration["Internal:ApiKey"];
if (string.IsNullOrWhiteSpace(inventoryBaseUrl) || string.IsNullOrWhiteSpace(internalApiKey))
    throw new InvalidOperationException("Inventory:BaseUrl and Internal:ApiKey must be configured (env Inventory__BaseUrl, Internal__ApiKey).");
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
{
    client.BaseAddress = new Uri(inventoryBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Inventory:TimeoutSeconds", 5));
    client.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalApiKey);
});

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    if (db.Database.IsNpgsql()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", context = "orders" }));
app.MapGet("/", () => Results.Ok(new { message = "Order service — orders API (see /orders)" }));

app.MapOrderEndpoints();

app.Run();

public partial class Program;
