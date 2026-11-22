using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using StatusTide.Api.Features.Catalog;
using StatusTide.Api.Features.Users;
using StatusTide.Common.Outbound;
using StatusTide.Contracts.CustomPages;

namespace StatusTide.Api.Features.CustomPages;

/// <summary>
/// Status pages accounts add by URL. Preview finds the feed and shows what it lists; add finds it
/// again (a preview could be old, or edited by the client) and follows all of it. Guests can't add
/// pages, since each one makes the server fetch a URL a stranger chose.
/// </summary>
[ApiController]
[Route("api/custom-pages")]
public class CustomPagesController(
    FeedDetection detection,
    AddCustomPageCommand addPage,
    CustomPagesQuery query,
    CatalogQuery catalog,
    MeQuery me) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CustomPagesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId)
        {
            return Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(await query.GetAsync(userId, cancellationToken));
    }

    [HttpPost("preview")]
    [EnableRateLimiting(CustomPagesExtensions.RateLimitPolicy)]
    [ProducesResponseType<CustomPagePreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Preview(CustomPageRequest request, CancellationToken cancellationToken)
    {
        if (await AccountAsync(cancellationToken) is not { } userId)
        {
            return NotAnAccount();
        }

        return await detection.DetectAsync(request.Url, cancellationToken) switch
        {
            FeedDetected feed => Ok(await query.PreviewAsync(userId, feed, cancellationToken)),
            var other => NotDetected(other),
        };
    }

    [HttpPost]
    [EnableRateLimiting(CustomPagesExtensions.RateLimitPolicy)]
    [ProducesResponseType<CatalogVendorDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Add(CustomPageRequest request, CancellationToken cancellationToken)
    {
        if (await AccountAsync(cancellationToken) is not { } userId)
        {
            return NotAnAccount();
        }

        var detected = await detection.DetectAsync(request.Url, cancellationToken);
        if (detected is not FeedDetected feed)
        {
            return NotDetected(detected);
        }

        var result = await addPage.ExecuteAsync(userId, feed, cancellationToken);
        return result.Outcome switch
        {
            AddCustomPageOutcome.Added => StatusCode(
                StatusCodes.Status201Created,
                await catalog.GetVendorAsync(userId, result.VendorId!.Value, cancellationToken)),
            AddCustomPageOutcome.NotAnAccount => NotAnAccount(),
            AddCustomPageOutcome.InCatalog => Problem(
                "That page is already in the catalog. Follow it from there.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["vendorId"] = result.VendorId }),
            AddCustomPageOutcome.CustomLimitReached => Problem(
                $"You can add up to {AddCustomPageCommand.CustomPageLimit} of your own pages.",
                statusCode: StatusCodes.Status409Conflict),
            AddCustomPageOutcome.LimitReached => Problem(
                "You've reached the limit of services you can follow.",
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}."),
        };
    }

    private async Task<Guid?> AccountAsync(CancellationToken cancellationToken) =>
        User.UserId() is { } userId && await me.GetAsync(userId, cancellationToken) is { IsGuest: false } ? userId : null;

    private ObjectResult NotAnAccount() => User.UserId() is null
        ? Problem("Start a guest session or sign in.", statusCode: StatusCodes.Status401Unauthorized)
        : Problem("Create an account to add your own status pages.", statusCode: StatusCodes.Status403Forbidden);

    private ObjectResult NotDetected(object? result) => result switch
    {
        FeedNotDetected notDetected => Problem(
            notDetected.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["reason"] = notDetected.Reason }),
        _ => Problem("We couldn't check that page right now. Try again in a minute.", statusCode: StatusCodes.Status503ServiceUnavailable),
    };
}

public record CustomPageRequest([Required][MaxLength(OutboundUrl.MaxLength)] string Url);
