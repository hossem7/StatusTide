using System.Security.Claims;

namespace StatusTide.Api.Features.Users;

/// <summary>
/// Who is calling. The user id lives in the standard NameIdentifier claim, which is also what
/// SignalR uses to route messages to one user. The guest cookie sets it today, and accounts
/// (#10) will set it too.
/// </summary>
public static class UserClaims
{
    /// <summary>The caller's user id, or null when they are not signed in.</summary>
    public static Guid? UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
