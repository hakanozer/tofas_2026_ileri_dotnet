using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EvolutionaryLogistics.BuildingBlocks.Auth;

public sealed class JwtSettings
{
    public const string Section = "Jwt";
    public const string UserIdClaim = "userId";
    public const string RoleClaim = "role";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    /// <summary>HMAC-SHA256 key; gateway ile aynı secret (env: Jwt__SigningKey), en az 32 bayt.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured (env Jwt__SigningKey) with at least 32 bytes.");
    }
}

public static class JwtAuthExtensions
{
    /// <summary>
    /// Yalnızca "Authorization: Bearer" kabul eder (cookie → Bearer dönüşümü ve CSRF kontrolü gateway'de yapılır).
    /// </summary>
    public static IServiceCollection AddServiceJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(JwtSettings.Section).Get<JwtSettings>() ?? new JwtSettings();
        settings.Validate();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtSettings.UserIdClaim,
                    RoleClaimType = JwtSettings.RoleClaim
                };
            });

        return services;
    }

    /// <summary>Kullanıcı kimliği yalnızca doğrulanmış JWT'deki "userId" claim'inden okunur.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(JwtSettings.UserIdClaim), out var id) && id != Guid.Empty ? id : null;
}
