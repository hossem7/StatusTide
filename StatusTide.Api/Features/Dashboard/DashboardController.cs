using Microsoft.AspNetCore.Mvc;
using StatusTide.Api.Features.Users;

namespace StatusTide.Api.Features.Dashboard;

/// <summary>
/// The dashboard snapshot. The page loads it once, then keeps it current with the cards that
/// <see cref="StatusHub"/> pushes.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController(DashboardQuery query) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in to see a dashboard.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(await query.GetAsync(userId, cancellationToken));
    }
}
