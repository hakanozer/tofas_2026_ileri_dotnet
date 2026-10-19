using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EvolutionaryLogistics.Gateway.Auth;

/// <summary>Design-time only (dotnet ef): uygulamayı (JWT key vb.) başlatmadan DbContext üretir.</summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                         ?? "Host=localhost;Database=evolutionary_logistics";
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connection, n => n.MigrationsHistoryTable("__ef_migrations", AuthDbContext.Schema))
            .Options;
        return new AuthDbContext(options);
    }
}
