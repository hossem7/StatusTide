using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

using StatusTide.Api.Features.Catalog;
using StatusTide.Api.Features.Users;

namespace StatusTide.Api.Features.Follows;

/// <summary>
/// Follow, change or stop following one vendor. The user always comes from the session, never
/// from the request, so one person can't change another's follows.
/// </summary>
[ApiController]
[Route("api/follows")]
public class FollowsController(SetFollowsCommand setFollows, CatalogQuery catalog) : ControllerBase
{
    /// <summary>Follows exactly the given components, or all of them when none are listed.</summary>
    [HttpPut("{vendorId:guid}")]
    [ProducesResponseType<CatalogVendorDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Set(Guid vendorId, SetFollowsRequest request, CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (request.ComponentIds is { Length: 0 })
        {
            return Problem("Choose at least one component, or unfollow the service.", statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await setFollows.ExecuteAsync(userId, vendorId, request.ComponentIds, cancellationToken);
        return outcome switch
        {
            SetFollowsOutcome.Saved => Ok(await catalog.GetVendorAsync(userId, vendorId, cancellationToken)),
            SetFollowsOutcome.NotSignedIn => Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized),
            SetFollowsOutcome.VendorNotFound => Problem("That service isn't in the catalog.", statusCode: StatusCodes.Status404NotFound),
            SetFollowsOutcome.UnknownComponents => Problem("Those components don't belong to this service.", statusCode: StatusCodes.Status400BadRequest),
            SetFollowsOutcome.LimitReached => Problem("You've reached the limit of services you can follow.", statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException($"Unhandled outcome {outcome}."),
        };
    }

    /// <summary>Stops following a vendor. Doing it twice is fine.</summary>
    [HttpDelete("{vendorId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unfollow(Guid vendorId, CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        await setFollows.UnfollowAsync(userId, vendorId, cancellationToken);
        return NoContent();
    }
}

/// <param name="ComponentIds">The components to follow. Null means all of them.</param>
public record SetFollowsRequest([MaxLength(500)] Guid[]? ComponentIds);
