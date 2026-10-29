using Microsoft.AspNetCore.Mvc;

namespace StatusTide.Api.Features.Users;

[ApiController]
[Route("api/me")]
public class MeController(MeQuery query) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId || await query.GetAsync(userId, cancellationToken) is not { } me)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(me);
    }
}
