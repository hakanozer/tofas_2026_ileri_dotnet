using EvolutionaryLogistics.BuildingBlocks.Auth;
using Order.Api.Caching;
using Order.Api.Data;

namespace Order.Api.Endpoints;

public sealed record CreateOrderItemRequest(string? Sku, int Quantity);

/// <summary>userId kasıtlı olarak yoktur: kullanıcı yalnızca JWT'den belirlenir.</summary>
public sealed record CreateOrderRequest(List<CreateOrderItemRequest>? Items);

public static class OrderEndpoints
{
    public const string OrdersPolicy = "orders";
    private const int MaxItems = 50;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").RequireAuthorization(OrdersPolicy);
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateOrderRequest request, HttpContext http, IOrderService orders, CancellationToken ct)
    {
        if (http.User.GetUserId() is not { } userId) return Results.Unauthorized();

        var items = request.Items ?? [];
        var errors = new Dictionary<string, string[]>();
        if (items.Count is 0 or > MaxItems)
            errors["items"] = [$"Between 1 and {MaxItems} items are required."];
        else if (items.Any(i => string.IsNullOrWhiteSpace(i.Sku) || i.Sku.Trim().Length > 64 || i.Quantity is < 1 or > 10_000))
            errors["items"] = ["Each item needs a sku (max 64 chars) and a quantity between 1 and 10000."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Aynı SKU birden fazla satırda gelirse birleştirilir.
        var merged = items
            .GroupBy(i => i.Sku!.Trim(), StringComparer.Ordinal)
            .Select(g => new OrderItemDto(g.Key, g.Sum(i => i.Quantity)))
            .ToList();
        if (merged.Any(i => i.Quantity > 10_000))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["Total quantity per sku must be at most 10000."] });

        var result = await orders.CreateAsync(userId, merged, ct);
        return result.Outcome switch
        {
            CreateOrderOutcome.Confirmed => Results.Created($"/orders/{result.Order.Id}", result.Order),
            CreateOrderOutcome.Rejected => Results.Json(result.Order, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(result.Order, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<IResult> ListAsync(HttpContext http, IOrderService orders, CancellationToken ct)
        => http.User.GetUserId() is { } userId
            ? Results.Ok(await orders.ListAsync(userId, ct))
            : Results.Unauthorized();

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, IOrderService orders, CancellationToken ct)
    {
        if (http.User.GetUserId() is not { } userId) return Results.Unauthorized();
        var order = await orders.GetAsync(userId, id, ct);
        return order is null ? Results.NotFound() : Results.Ok(order);
    }
}
