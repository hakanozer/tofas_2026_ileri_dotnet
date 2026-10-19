using Microsoft.EntityFrameworkCore;
using Order.Api.Caching;
using Order.Api.Clients;

namespace Order.Api.Data;

public enum CreateOrderOutcome { Confirmed, Rejected, InventoryUnavailable }

public sealed record CreateOrderResult(CreateOrderOutcome Outcome, OrderDto Order);

public interface IOrderService
{
    Task<CreateOrderResult> CreateAsync(Guid userId, IReadOnlyList<OrderItemDto> items, CancellationToken ct);
    Task<IReadOnlyList<OrderDto>> ListAsync(Guid userId, CancellationToken ct);
    Task<OrderDto?> GetAsync(Guid userId, Guid orderId, CancellationToken ct);
}

public sealed class OrderService(
    OrderDbContext db,
    IInventoryClient inventory,
    IOrderListCache cache,
    ILogger<OrderService> logger) : IOrderService
{
    private const int MaxListSize = 100;

    public async Task<CreateOrderResult> CreateAsync(Guid userId, IReadOnlyList<OrderItemDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var order = new CustomerOrder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
            Items = items.Select(i => new OrderItem { Id = Guid.NewGuid(), Sku = i.Sku, Quantity = i.Quantity }).ToList()
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(userId, ct);
        logger.LogInformation("Order {OrderId} created for user {UserId} (Pending)", order.Id, userId);

        var reserve = await inventory.ReserveAsync(order.Id, items, ct);

        CreateOrderOutcome outcome;
        switch (reserve.Outcome)
        {
            case InventoryOutcome.Reserved:
                order.Confirm();
                outcome = CreateOrderOutcome.Confirmed;
                break;
            case InventoryOutcome.Insufficient:
                order.Reject(DescribeShortages(reserve.Shortages));
                outcome = CreateOrderOutcome.Rejected;
                break;
            default:
                order.Reject("Inventory service unavailable.");
                outcome = CreateOrderOutcome.InventoryUnavailable;
                // Cevap kaybolmuş olsa bile rezervasyon kalmasın (release idempotenttir).
                await inventory.ReleaseAsync(order.Id, CancellationToken.None);
                break;
        }

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            if (outcome == CreateOrderOutcome.Confirmed) await inventory.ReleaseAsync(order.Id, CancellationToken.None);
            throw;
        }
        finally
        {
            await cache.InvalidateAsync(userId, CancellationToken.None);
        }

        logger.LogInformation("Order {OrderId} is now {Status}", order.Id, order.Status);
        return new CreateOrderResult(outcome, OrderDto.From(order));
    }

    public async Task<IReadOnlyList<OrderDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        var cached = await cache.GetAsync(userId, ct);
        if (cached is not null)
        {
            logger.LogDebug("Order list cache hit for user {UserId}", userId);
            return cached;
        }

        var orders = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxListSize)
            .ToListAsync(ct);

        var dtos = orders.Select(OrderDto.From).ToList();
        await cache.SetAsync(userId, dtos, ct);
        logger.LogDebug("Order list cache miss for user {UserId}", userId);
        return dtos;
    }

    public async Task<OrderDto?> GetAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        // userId filtresi sorguda: başka kullanıcının siparişi "yok" gibi davranır (varlık sızdırılmaz).
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct);
        return order is null ? null : OrderDto.From(order);
    }

    private static string DescribeShortages(IReadOnlyList<StockShortage> shortages)
        => shortages.Count == 0
            ? "Insufficient stock."
            : "Insufficient stock: " + string.Join("; ", shortages.Select(s => $"{s.Sku} (requested {s.Requested}, available {s.Available})"));
}
