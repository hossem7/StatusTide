using Microsoft.AspNetCore.Mvc;
using StatusTide.Api.Features.Users;

namespace StatusTide.Api.Features.Incidents;

/// <summary>The incident page's data. Needs a session, since it marks the components the caller follows.</summary>
[ApiController]
[Route("api/incidents")]
public class IncidentsController(IncidentQuery query) : ControllerBase
{
    [HttpGet("{incidentId:guid}")]
    [ProducesResponseType<IncidentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid incidentId, CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        // The same answer for a missing incident and one the caller may not see, so ids can't be probed.
        return await query.GetAsync(userId, incidentId, cancellationToken) is { } incident
            ? Ok(incident)
            : Problem("We couldn't find that incident.", statusCode: StatusCodes.Status404NotFound);
    }
}
