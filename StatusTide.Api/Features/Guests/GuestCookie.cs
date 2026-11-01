using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Guests;

public static class GuestCookie
{
    /// <summary>
    /// The __Host- prefix makes browsers refuse the cookie unless it is Secure, has Path=/ and no
    /// Domain, so a sibling subdomain can't set or overwrite it.
    /// </summary>
    public const string Name = "__Host-statustide-guest";

    /// <summary>Sets the cookie to expire <see cref="GuestPolicy.Lifetime"/> after <paramref name="now"/>.</summary>
    public static void Write(HttpResponse response, string token, DateTimeOffset now) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = now + GuestPolicy.Lifetime,
            IsEssential = true,
        });

    /// <summary>Browsers ignore the removal of a __Host- cookie unless it is Secure with Path=/.</summary>
    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true });
}
