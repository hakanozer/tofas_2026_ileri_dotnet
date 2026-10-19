namespace EvolutionaryLogistics.Gateway.Auth;

public static class AppRoles
{
    public const string Ai = "ai";
    public const string Inventory = "inventory";
    public const string Order = "order";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>([Ai, Inventory, Order], StringComparer.Ordinal);
}

public static class AuthPolicies
{
    public const string Authenticated = "authenticated";
    public const string AiAccess = "ai-access";
    public const string InventoryAccess = "inventory-access";
    public const string OrderAccess = "order-access";
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<AppUserRole> Roles { get; set; } = [];
}

public sealed class AppUserRole
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public AppUser? User { get; set; }
}

public sealed record RegisterRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Password,
    string[]? Roles);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string[] Roles);

public sealed record LoginResponse(UserResponse User, DateTimeOffset ExpiresAt, string CsrfToken);
