namespace StatusTide.Api.Features.Accounts;

/// <summary>
/// The refresh token's cookie. SameSite=Strict keeps other sites from triggering a refresh or a
/// sign-out, and the path means no other endpoint ever receives it. A __Host- cookie must have
/// Path=/, so this one uses the __Secure- prefix.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "__Secure-statustide-refresh";
    public const string Path = "/api/auth";

    public static void Write(HttpResponse response, IssuedRefreshToken refresh) =>
        response.Cookies.Append(Name, refresh.Token, Options(refresh.ExpiresAt));

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
