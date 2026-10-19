using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Order.Api.Data;

/// <summary>Design-time only (dotnet ef): uygulamayı başlatmadan DbContext üretir.</summary>
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
                         ?? "Host=localhost;Database=order_db";
        return new OrderDbContext(new DbContextOptionsBuilder<OrderDbContext>().UseNpgsql(connection).Options);
    }
}
