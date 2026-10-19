using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EvolutionaryLogistics.Services.Inventory.Data;

/// <summary>Design-time only (dotnet ef): uygulamayı başlatmadan DbContext üretir.</summary>
public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
                         ?? "Host=localhost;Database=inventory_db";
        return new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connection).Options);
    }
}
