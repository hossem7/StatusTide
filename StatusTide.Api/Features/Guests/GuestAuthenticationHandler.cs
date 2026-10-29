using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Guests;

/// <summary>
/// Signs a request in as the guest whose token is in the cookie. The token is checked against
/// the database on every request, so a guest the cleanup job deleted is signed out at once.
/// At most once per <see cref="GuestPolicy.RenewAfter"/>, a visit moves the guest's last seen
/// time and the cookie's expiry forward.
/// </summary>
public class GuestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    StatusTideDbContext db,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Guest";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[GuestCookie.Name];
        if (GuestToken.Hash(token) is not { } hash)
        {
            return AuthenticateResult.NoResult();
        }

        var guest = await db.Users
            .AsNoTracking()
            .Where(u => u.IsGuest && u.GuestTokenHash == hash)
            .Select(u => new { u.Id, u.LastSeenAt })
            .SingleOrDefaultAsync(Context.RequestAborted);
        if (guest is null)
        {
            // Unknown or deleted guest. Not a failure: the request just isn't signed in.
            return AuthenticateResult.NoResult();
        }

        var now = timeProvider.GetUtcNow();
        if (now - guest.LastSeenAt >= GuestPolicy.RenewAfter)
        {
            await db.Users
                .Where(u => u.Id == guest.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, now), Context.RequestAborted);
            GuestCookie.Write(Response, token!, now);
        }

        return AuthenticateResult.Success(new AuthenticationTicket(Principal(guest.Id), SchemeName));
    }

    public static ClaimsPrincipal Principal(Guid userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], SchemeName));
}
