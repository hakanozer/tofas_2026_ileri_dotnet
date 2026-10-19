using Microsoft.EntityFrameworkCore;

namespace EvolutionaryLogistics.Services.Inventory.Data;

public sealed class StockItem
{
    public string Sku { get; set; } = string.Empty;
    public int Available { get; set; }
    public int Reserved { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class ReservationStatus
{
    public const string Reserved = "Reserved";
    public const string Released = "Released";
}

/// <summary>Bir siparişe ait rezervasyon; Id = OrderId olduğundan istek idempotenttir.</summary>
public sealed class Reservation
{
    public Guid Id { get; set; }
    public string Status { get; set; } = ReservationStatus.Reserved;
    public DateTime CreatedAt { get; set; }
    public List<ReservationLine> Lines { get; set; } = [];
}

public sealed class ReservationLine
{
    public Guid ReservationId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(e =>
        {
            e.ToTable("stock_items", t =>
            {
                t.HasCheckConstraint("ck_stock_available", "\"Available\" >= 0");
                t.HasCheckConstraint("ck_stock_reserved", "\"Reserved\" >= 0");
            });
            e.HasKey(x => x.Sku);
            e.Property(x => x.Sku).HasMaxLength(64);
        });

        modelBuilder.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReservationLine>(e =>
        {
            e.ToTable("reservation_lines");
            e.HasKey(x => new { x.ReservationId, x.Sku });
            e.Property(x => x.Sku).HasMaxLength(64);
        });
    }
}
