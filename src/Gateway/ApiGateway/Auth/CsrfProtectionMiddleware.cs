using System.Security.Cryptography;
using System.Text;

namespace EvolutionaryLogistics.Gateway.Auth;

/// <summary>
/// Kimlik doğrulama cookie'den geldiyse, state-değiştiren isteklerde X-CSRF-TOKEN header'ının
/// JWT içindeki "csrf" claim'i ile eşleşmesini zorunlu kılar (SameSite'a ek savunma katmanı).
/// Authorization header ile gelen Bearer istekleri tarayıcı tarafından otomatik eklenmediği için muaftır.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-CSRF-TOKEN";
    // Logout her zaman cookie'leri temizlemeli (süresi dolmuş token / eksik CSRF olsa bile); çıkış zorlamasının riski düşüktür.
    private const string LogoutPath = "/api/auth/logout";
    public const string FromCookieItem = "auth.from-cookie";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
            && !HttpMethods.IsTrace(context.Request.Method)
            && !context.Request.Path.Equals(LogoutPath, StringComparison.OrdinalIgnoreCase)
            && context.Items.ContainsKey(FromCookieItem)
            && context.User.Identity?.IsAuthenticated == true)
        {
            var expected = context.User.FindFirst(JwtTokenService.CsrfClaim)?.Value;
            var actual = context.Request.Headers[HeaderName].FirstOrDefault();

            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = 403,
                    title = "Missing or invalid CSRF token."
                });
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
