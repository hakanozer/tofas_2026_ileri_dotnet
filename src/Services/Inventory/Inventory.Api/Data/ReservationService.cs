using Microsoft.EntityFrameworkCore;

namespace EvolutionaryLogistics.Services.Inventory.Data;

public sealed record ReserveLine(string Sku, int Quantity);

public sealed record ShortageDto(string Sku, int Requested, int Available);

public enum ReserveOutcome { Reserved, Insufficient, AlreadyReleased }

public sealed record ReserveResult(ReserveOutcome Outcome, IReadOnlyList<ShortageDto> Shortages);

public interface IReservationService
{
    Task<ReserveResult> ReserveAsync(Guid orderId, IReadOnlyList<ReserveLine> lines, CancellationToken ct);
    Task<bool> ReleaseAsync(Guid orderId, CancellationToken ct);
}

public sealed class ReservationService(InventoryDbContext db, ILogger<ReservationService> logger) : IReservationService
{
    public async Task<ReserveResult> ReserveAsync(Guid orderId, IReadOnlyList<ReserveLine> lines, CancellationToken ct)
    {
        var existing = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == orderId, ct);
        if (existing is not null) return ExistingResult(existing);

        // Sıralı kilit: aynı SKU'ları ters sırada isteyen iki sipariş deadlock'a girmesin.
        var ordered = lines.OrderBy(l => l.Sku, StringComparer.Ordinal).ToList();
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in ordered)
        {
            var quantity = line.Quantity;
            // Koşullu UPDATE atomiktir: stok yetersizse hiçbir satır etkilenmez (oversell olmaz).
            var rows = await db.Stock
                .Where(s => s.Sku == line.Sku && s.Available >= quantity)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Available, x => x.Available - quantity)
                    .SetProperty(x => x.Reserved, x => x.Reserved + quantity)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                var shortages = await ShortagesAsync(ordered, ct);
                logger.LogInformation("Reservation for order {OrderId} rejected: insufficient stock for {Skus}",
                    orderId, string.Join(',', shortages.Select(s => s.Sku)));
                return new ReserveResult(ReserveOutcome.Insufficient, shortages);
            }
        }

        db.Reservations.Add(new Reservation
        {
            Id = orderId,
            Status = ReservationStatus.Reserved,
            CreatedAt = now,
            Lines = ordered.Select(l => new ReservationLine { ReservationId = orderId, Sku = l.Sku, Quantity = l.Quantity }).ToList()
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı orderId ile eşzamanlı istek kazandı: tx geri alınır, mevcut rezervasyon döndürülür.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == orderId, ct);
            if (winner is not null) return ExistingResult(winner);
            throw;
        }

        logger.LogInformation("Stock reserved for order {OrderId} ({Count} lines)", orderId, ordered.Count);
        return new ReserveResult(ReserveOutcome.Reserved, []);
    }

    public async Task<bool> ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        var reservation = await db.Reservations.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == orderId, ct);
        if (reservation is null) return false;
        if (reservation.Status == ReservationStatus.Released) return true;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var line in reservation.Lines)
        {
            var quantity = line.Quantity;
            await db.Stock.Where(s => s.Sku == line.Sku)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Available, x => x.Available + quantity)
                    .SetProperty(x => x.Reserved, x => x.Reserved - quantity)
                    .SetProperty(x => x.UpdatedAt, now), ct);
        }

        reservation.Status = ReservationStatus.Released;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Reservation for order {OrderId} released", orderId);
        return true;
    }

    private static ReserveResult ExistingResult(Reservation r)
        => r.Status == ReservationStatus.Reserved
            ? new ReserveResult(ReserveOutcome.Reserved, [])
            : new ReserveResult(ReserveOutcome.AlreadyReleased, []);

    private async Task<IReadOnlyList<ShortageDto>> ShortagesAsync(List<ReserveLine> lines, CancellationToken ct)
    {
        var skus = lines.Select(l => l.Sku).ToList();
        var stock = await db.Stock.AsNoTracking().Where(s => skus.Contains(s.Sku))
            .ToDictionaryAsync(s => s.Sku, s => s.Available, ct);
        return lines
            .Select(l => new ShortageDto(l.Sku, l.Quantity, stock.GetValueOrDefault(l.Sku)))
            .Where(s => s.Available < s.Requested)
            .ToList();
    }
}
