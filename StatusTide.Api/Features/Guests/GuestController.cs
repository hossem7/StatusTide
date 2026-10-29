using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using StatusTide.Api.Features.Users;

namespace StatusTide.Api.Features.Guests;

/// <summary>"Try it as a guest": starts a guest session and sets the guest cookie.</summary>
[ApiController]
[Route("api/guest")]
public class GuestController(StartGuestCommand startGuest, MeQuery me, TimeProvider timeProvider) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(GuestExtensions.RateLimitPolicy)]
    [ProducesResponseType<MeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        // A second click, or a second tab, keeps the session it already has.
        if (User.UserId() is { } userId && await me.GetAsync(userId, cancellationToken) is { } current)
        {
            return Ok(current);
        }

        var (guest, token) = await startGuest.ExecuteAsync(cancellationToken);
        GuestCookie.Write(Response, token, timeProvider.GetUtcNow());
        return StatusCode(StatusCodes.Status201Created, MeDto.From(guest));
    }
}
