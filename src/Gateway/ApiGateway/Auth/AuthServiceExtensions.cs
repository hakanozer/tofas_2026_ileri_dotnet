using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace EvolutionaryLogistics.Gateway.Auth;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddGatewayAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        jwt.Validate();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", AuthDbContext.Schema)));

        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ITokenSessionStore, RedisTokenSessionStore>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.CreateKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    // İmza geçerli olsa bile Redis'te oturum kaydı yoksa (logout/süre dolumu) token reddedilir.
                    // Redis erişilemezse fail-closed: doğrulanamayan oturum kabul edilmez.
                    OnTokenValidated = async ctx =>
                    {
                        var jti = ctx.Principal?.FindFirst("jti")?.Value;
                        var store = ctx.HttpContext.RequestServices.GetRequiredService<ITokenSessionStore>();
                        try
                        {
                            if (string.IsNullOrEmpty(jti) || !await store.IsActiveAsync(jti, ctx.HttpContext.RequestAborted))
                                ctx.Fail("Session is not active.");
                        }
                        catch (RedisException ex)
                        {
                            ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Auth.Session").LogError(ex, "Redis session check failed; rejecting token.");
                            ctx.Fail("Session store unavailable.");
                        }
                    },
                    // Authorization header yoksa HttpOnly cookie'den oku.
                    OnMessageReceived = ctx =>
                    {
                        if (string.IsNullOrEmpty(ctx.Request.Headers.Authorization)
                            && ctx.Request.Cookies.TryGetValue(jwt.CookieName, out var token)
                            && !string.IsNullOrEmpty(token))
                        {
                            ctx.Token = token;
                            // Downstream servisler yalnızca Bearer kabul eder; CSRF kontrolü gateway'de (cookie'li istekler için) yapılır.
                            ctx.Request.Headers.Authorization = $"Bearer {token}";
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Authenticated, p => p.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.AiAccess, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Ai))
            .AddPolicy(AuthPolicies.InventoryAccess, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Inventory))
            .AddPolicy(AuthPolicies.OrderAccess, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Order));

        return services;
    }

    public static async Task MigrateAuthDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync();
    }
}
