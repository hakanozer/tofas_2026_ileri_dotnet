using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EvolutionaryLogistics.Gateway.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    /// <summary>HMAC-SHA256 key; must come from configuration/secret store (min 32 bytes).</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 600;
    public string CookieName { get; set; } = "access_token";
    public string CsrfCookieName { get; set; } = "XSRF-TOKEN";
    public string CookieSameSite { get; set; } = "Strict";

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured (env Jwt__SigningKey) with at least 32 bytes.");
        if (ExpiryMinutes <= 0)
            throw new InvalidOperationException("Jwt:ExpiryMinutes must be positive.");
        if (!Enum.TryParse<SameSiteMode>(CookieSameSite, true, out _))
            throw new InvalidOperationException("Jwt:CookieSameSite must be Strict, Lax or None.");
    }
}

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt, string CsrfToken);

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public const string CsrfClaim = "csrf";
    private readonly JwtOptions _options = options.Value;

    public IssuedToken Issue(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.ExpiryMinutes);
        var csrf = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("sub", user.Id.ToString()));
        identity.AddClaim(new Claim("userId", user.Id.ToString()));
        identity.AddClaim(new Claim("email", user.Email));
        identity.AddClaim(new Claim("given_name", user.FirstName));
        identity.AddClaim(new Claim("family_name", user.LastName));
        identity.AddClaim(new Claim(CsrfClaim, csrf));
        foreach (var role in user.Roles.Select(r => r.Role).Distinct(StringComparer.Ordinal))
            identity.AddClaim(new Claim("role", role));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_options.CreateKey(), SecurityAlgorithms.HmacSha256)
        });

        return new IssuedToken(token, expires, csrf);
    }
}
