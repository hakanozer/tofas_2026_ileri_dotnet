using Microsoft.EntityFrameworkCore;

namespace EvolutionaryLogistics.Services.Inventory.Data;

/// <summary>Yalnızca Development: boş veritabanına demo stok ekler.</summary>
public static class InventorySeed
{
    public static async Task SeedAsync(InventoryDbContext db)
    {
        if (await db.Stock.AnyAsync()) return;

        var now = DateTime.UtcNow;
        db.Stock.AddRange(
            new StockItem { Sku = "SKU-1001", Available = 100, UpdatedAt = now },
            new StockItem { Sku = "SKU-1002", Available = 50, UpdatedAt = now },
            new StockItem { Sku = "SKU-1003", Available = 5, UpdatedAt = now });
        await db.SaveChangesAsync();
    }
}
