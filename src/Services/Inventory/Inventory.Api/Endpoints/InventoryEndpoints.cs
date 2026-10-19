using System.Security.Cryptography;
using System.Text;
using EvolutionaryLogistics.Services.Inventory.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EvolutionaryLogistics.Services.Inventory.Endpoints;

public sealed class InternalApiOptions
{
    public const string Section = "Internal";
    public const string HeaderName = "X-Internal-Api-Key";

    /// <summary>Servisler arası paylaşılan secret (env: Internal__ApiKey).</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public sealed record StockResponse(string Sku, int Available, int Reserved, DateTime UpdatedAt);
public sealed record SetStockRequest(int? Quantity);
public sealed record ReserveItemRequest(string? Sku, int Quantity);
public sealed record ReserveRequest(Guid OrderId, List<ReserveItemRequest>? Items);
public sealed record ReserveResponse(Guid OrderId, string Status, IReadOnlyList<ShortageDto> Shortages);

public static class InventoryEndpoints
{
    public const string InventoryPolicy = "inventory";
    private const int MaxItems = 50;

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/stock").RequireAuthorization(InventoryPolicy);
        stock.MapGet("/", ListStockAsync);
        stock.MapGet("/{sku}", GetStockAsync);
        stock.MapPut("/{sku}", SetStockAsync);

        // Yalnızca servisten servise: JWT değil, paylaşılan API key. Gateway üzerinden de erişilse key olmadan 401 döner.
        var internalApi = app.MapGroup("/internal/reservations").AddEndpointFilter(InternalKeyFilterAsync);
        internalApi.MapPost("/", ReserveAsync);
        internalApi.MapDelete("/{orderId:guid}", ReleaseAsync);

        return app;
    }

    private static async ValueTask<object?> InternalKeyFilterAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<InternalApiOptions>>().Value.ApiKey;
        var actual = ctx.HttpContext.Request.Headers[InternalApiOptions.HeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid internal API key.");
        }

        return await next(ctx);
    }

    private static async Task<IResult> ListStockAsync(InventoryDbContext db, CancellationToken ct)
    {
        var items = await db.Stock.AsNoTracking().OrderBy(s => s.Sku).Take(500).ToListAsync(ct);
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> GetStockAsync(string sku, InventoryDbContext db, CancellationToken ct)
    {
        var item = await db.Stock.AsNoTracking().FirstOrDefaultAsync(s => s.Sku == sku, ct);
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> SetStockAsync(string sku, SetStockRequest request, InventoryDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(sku) || sku.Length > 64) errors["sku"] = ["Sku is required (max 64 chars)."];
        if (request.Quantity is null or < 0 or > 1_000_000) errors["quantity"] = ["Quantity must be between 0 and 1000000."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Available yalnızca mevcut rezervasyonlardan bağımsız "fiziksel serbest stok" olarak ayarlanır.
        var now = DateTime.UtcNow;
        var updated = await db.Stock.Where(s => s.Sku == sku)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Available, request.Quantity!.Value).SetProperty(x => x.UpdatedAt, now), ct);
        if (updated == 0)
        {
            db.Stock.Add(new StockItem { Sku = sku, Available = request.Quantity!.Value, UpdatedAt = now });
            await db.SaveChangesAsync(ct);
        }

        var item = await db.Stock.AsNoTracking().FirstAsync(s => s.Sku == sku, ct);
        return Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> ReserveAsync(ReserveRequest request, IReservationService service, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.OrderId == Guid.Empty) errors["orderId"] = ["OrderId is required."];
        var items = request.Items ?? [];
        if (items.Count is 0 or > MaxItems) errors["items"] = [$"Between 1 and {MaxItems} items are required."];
        else if (items.Any(i => string.IsNullOrWhiteSpace(i.Sku) || i.Sku.Length > 64 || i.Quantity is < 1 or > 10_000)
                 || items.Select(i => i.Sku).Distinct().Count() != items.Count)
            errors["items"] = ["Each item needs a unique sku (max 64 chars) and quantity between 1 and 10000."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var result = await service.ReserveAsync(request.OrderId,
            items.Select(i => new ReserveLine(i.Sku!, i.Quantity)).ToList(), ct);

        return result.Outcome switch
        {
            ReserveOutcome.Reserved => Results.Ok(new ReserveResponse(request.OrderId, "Reserved", [])),
            ReserveOutcome.AlreadyReleased => Results.Json(
                new ReserveResponse(request.OrderId, "Released", []), statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(
                new ReserveResponse(request.OrderId, "Insufficient", result.Shortages), statusCode: StatusCodes.Status409Conflict)
        };
    }

    private static async Task<IResult> ReleaseAsync(Guid orderId, IReservationService service, CancellationToken ct)
        => await service.ReleaseAsync(orderId, ct) ? Results.NoContent() : Results.NotFound();

    private static StockResponse ToResponse(StockItem s) => new(s.Sku, s.Available, s.Reserved, s.UpdatedAt);
}
