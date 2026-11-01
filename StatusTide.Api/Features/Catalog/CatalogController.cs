using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

using StatusTide.Api.Features.Users;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Catalog;

/// <summary>The "Add services" screen's data. Needs a session, since it shows what the caller follows.</summary>
[ApiController]
[Route("api/catalog")]
public class CatalogController(CatalogQuery query) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CatalogDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(
        [FromQuery, MaxLength(100)] string? search,
        [FromQuery] VendorCategory? category,
        CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(await query.GetAsync(userId, search, category, cancellationToken));
    }

    [HttpGet("{vendorId:guid}")]
    [ProducesResponseType<CatalogVendorDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVendor(Guid vendorId, CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return await query.GetVendorAsync(userId, vendorId, cancellationToken) is { } vendor
            ? Ok(vendor)
            : Problem("That service isn't in the catalog.", statusCode: StatusCodes.Status404NotFound);
    }
}
