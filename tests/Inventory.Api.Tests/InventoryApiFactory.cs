using System.Net.Http.Headers;
using System.Text;
using EvolutionaryLogistics.Services.Inventory.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Api.Tests;

public sealed class InventoryApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "test-issuer";
    public const string Audience = "test-audience";
    public const string SigningKey = "test-signing-key-test-signing-key-0123456789";
    public const string InternalKey = "test-internal-key";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public InventoryApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:InventoryDb", "Host=unused;Database=unused");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Internal:ApiKey", InternalKey);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<InventoryDbContext>));
            services.AddDbContext<InventoryDbContext>(o => o.UseSqlite(_connection));
        });
    }

    public async Task SeedAsync(string sku, int available)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        db.Stock.Add(new StockItem { Sku = sku, Available = available, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task<StockItem> GetStockAsync(string sku)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.Stock.AsNoTracking().FirstAsync(s => s.Sku == sku);
    }

    public HttpClient CreateInternalClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", InternalKey);
        return client;
    }

    public HttpClient CreateUserClient(params string[] roles)
    {
        var client = CreateClient();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["userId"] = Guid.NewGuid().ToString(), ["role"] = roles },
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
