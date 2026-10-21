using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace EvolutionaryLogistics.Gateway.Auth;

public static class AuthEndpoints
{
    // Kullanıcı yokken de hash doğrulaması yapılarak zamanlama farkından kullanıcı tespiti engellenir.
    private static readonly string DummyHash =
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), "dummy-Password-1");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", Me).RequireAuthorization(AuthPolicies.Authenticated);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        AuthDbContext db,
        IPasswordHasher<AppUser> hasher,
        IHostEnvironment env,
        HttpContext http,
        CancellationToken ct)
    {
        if (RequireSecureTransport(http, env) is { } insecure) return insecure;

        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var email = request.Email!.Trim();
        var normalized = email.ToUpperInvariant();

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            return DuplicateEmail();

        var roles = (request.Roles ?? []).Select(r => r.Trim().ToLowerInvariant()).Distinct().ToArray();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName!.Trim(),
            LastName = request.LastName!.Trim(),
            Email = email,
            NormalizedEmail = normalized,
            CreatedAt = DateTimeOffset.UtcNow,
            Roles = roles.Select(r => new AppUserRole { Role = r }).ToList()
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password!);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return DuplicateEmail();
        }

        return Results.Created($"/api/auth/users/{user.Id}", ToResponse(user));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthDbContext db,
        IPasswordHasher<AppUser> hasher,
        JwtTokenService tokens,
        ITokenSessionStore sessions,
        IOptions<JwtOptions> jwtOptions,
        IHostEnvironment env,
        HttpContext http,
        CancellationToken ct)
    {
        int i = 0;
        int ii = 1 / i;
        if (RequireSecureTransport(http, env) is { } insecure) return insecure;

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email)) errors["email"] = ["Email is required."];
        if (string.IsNullOrEmpty(request.Password)) errors["password"] = ["Password is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var normalized = request.Email!.Trim().ToUpperInvariant();
        var user = await db.Users.Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        var result = hasher.VerifyHashedPassword(user ?? new AppUser(), user?.PasswordHash ?? DummyHash, request.Password!);
        if (user is null || result == PasswordVerificationResult.Failed)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            await db.SaveChangesAsync(ct);
        }

        var issued = tokens.Issue(user);
        try
        {
            // Oturum Redis'e token ömrü kadar TTL ile yazılır; sonraki her istekte buradan doğrulanır.
            await sessions.StoreAsync(issued.Jti, user.Id, issued.ExpiresAt - DateTimeOffset.UtcNow, ct);
        }
        catch (RedisException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Session store unavailable.");
        }
        var options = jwtOptions.Value;
        var secure = !env.IsDevelopment() || http.Request.IsHttps;
        var sameSite = Enum.Parse<SameSiteMode>(options.CookieSameSite, true);

        http.Response.Cookies.Append(options.CookieName, issued.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Expires = issued.ExpiresAt,
            IsEssential = true,
            Path = "/"
        });
        // JS okuyabilmeli: double-submit CSRF değeri (JWT içindeki "csrf" claim'i ile eşleşmeli).
        http.Response.Cookies.Append(options.CsrfCookieName, issued.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = sameSite,
            Expires = issued.ExpiresAt,
            IsEssential = true,
            Path = "/"
        });
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new LoginResponse(ToResponse(user), issued.ExpiresAt, issued.CsrfToken));
    }

    private static async Task<IResult> Logout(
        HttpContext http, ITokenSessionStore sessions, IOptions<JwtOptions> jwtOptions, IHostEnvironment env, CancellationToken ct)
    {
        // Geçerli bir oturum varsa Redis kaydı silinir; token çalınmış olsa bile artık reddedilir.
        if (http.User.FindFirst("jti")?.Value is { Length: > 0 } jti)
        {
            try { await sessions.RevokeAsync(jti, ct); }
            catch (RedisException)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Session store unavailable.");
            }
        }

        var options = jwtOptions.Value;
        var secure = !env.IsDevelopment() || http.Request.IsHttps;
        var sameSite = Enum.Parse<SameSiteMode>(options.CookieSameSite, true);
        http.Response.Cookies.Delete(options.CookieName,
            new CookieOptions { HttpOnly = true, Secure = secure, SameSite = sameSite, Path = "/" });
        http.Response.Cookies.Delete(options.CsrfCookieName,
            new CookieOptions { Secure = secure, SameSite = sameSite, Path = "/" });
        http.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static IResult Me(ClaimsPrincipal principal) => Results.Ok(new
    {
        id = principal.FindFirstValue("sub"),
        email = principal.FindFirstValue("email"),
        firstName = principal.FindFirstValue("given_name"),
        lastName = principal.FindFirstValue("family_name"),
        roles = principal.FindAll("role").Select(c => c.Value).ToArray()
    });

    private static IResult? RequireSecureTransport(HttpContext http, IHostEnvironment env)
        => env.IsDevelopment() || http.Request.IsHttps
            ? null
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "HTTPS is required.");

    private static IResult DuplicateEmail()
        => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email is already registered.");

    private static UserResponse ToResponse(AppUser u) => new(
        u.Id, u.FirstName, u.LastName, u.Email,
        u.Roles.Select(r => r.Role).OrderBy(r => r, StringComparer.Ordinal).ToArray());

    private static Dictionary<string, string[]> Validate(RegisterRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(r.FirstName)) errors["firstName"] = ["FirstName is required."];
        else if (r.FirstName.Trim().Length > 100) errors["firstName"] = ["FirstName must be at most 100 characters."];

        if (string.IsNullOrWhiteSpace(r.LastName)) errors["lastName"] = ["LastName is required."];
        else if (r.LastName.Trim().Length > 100) errors["lastName"] = ["LastName must be at most 100 characters."];

        if (string.IsNullOrWhiteSpace(r.Email)) errors["email"] = ["Email is required."];
        else if (!IsValidEmail(r.Email.Trim())) errors["email"] = ["Email is not valid."];

        var passwordErrors = ValidatePassword(r.Password);
        if (passwordErrors.Count > 0) errors["password"] = [.. passwordErrors];

        var invalid = (r.Roles ?? [])
            .Where(role => role is null || !AppRoles.Allowed.Contains(role.Trim().ToLowerInvariant()))
            .ToArray();
        if (invalid.Length > 0)
            errors["roles"] = [$"Invalid role(s). Allowed: {string.Join(", ", AppRoles.Allowed.Order())}."];

        return errors;
    }

    private static bool IsValidEmail(string email)
        => email.Length <= 254
           && MailAddress.TryCreate(email, out var parsed)
           && parsed.Address == email
           && parsed.Host.Contains('.');

    private static List<string> ValidatePassword(string? password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password)) return ["Password is required."];
        if (password.Length < 8) errors.Add("Password must be at least 8 characters.");
        if (password.Length > 128) errors.Add("Password must be at most 128 characters.");
        if (!password.Any(char.IsUpper)) errors.Add("Password must contain an uppercase letter.");
        if (!password.Any(char.IsLower)) errors.Add("Password must contain a lowercase letter.");
        if (!password.Any(char.IsDigit)) errors.Add("Password must contain a digit.");
        return errors;
    }
}
