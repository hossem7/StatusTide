using MassTransit;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using StatusTide.Common.Demo;
using StatusTide.Contracts.Polling;
using StatusTide.Data.Seed;

namespace StatusTide.Api.Features.Demo;

/// <summary>
/// "Simulate an outage" for the demo vendor, Acme Cloud. Starting or resolving an outage queues
/// a poll right away, so the change reaches dashboards in seconds instead of at the next
/// scheduled poll. The outage is shared by every visitor.
/// </summary>
[ApiController]
[Route("api/demo/outage")]
public class DemoController(DemoOutageStore store, IPublishEndpoint publisher, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<DemoOutageDto> Get() => DemoOutageDto.From(await store.GetAsync(), timeProvider.GetUtcNow());

    [HttpPost]
    [EnableRateLimiting(DemoExtensions.RateLimitPolicy)]
    [ProducesResponseType<DemoOutageDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (outage, started) = await store.TryStartAsync();
        if (!started)
        {
            var wait = outage is null ? TimeSpan.Zero : outage.CooldownEndsAt - now;
            Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return Problem(
                outage?.IsActiveAt(now) == true ? "A simulated outage is already running." : "The demo is cooling down after the last outage.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await QueuePollAsync(now, cancellationToken);
        return Accepted(DemoOutageDto.From(outage, now));
    }

    [HttpDelete]
    [EnableRateLimiting(DemoExtensions.RateLimitPolicy)]
    [ProducesResponseType<DemoOutageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (await store.ResolveAsync() is not { } outage)
        {
            return Problem("No simulated outage is running.", statusCode: StatusCodes.Status404NotFound);
        }

        await QueuePollAsync(now, cancellationToken);
        return Ok(DemoOutageDto.From(outage, outage.ResolvesAt));
    }

    // An extra poll on top of the scheduled ones. Polls are idempotent, so this is safe.
    private Task QueuePollAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        publisher.Publish(new PollVendor(AcmeCloudSeed.VendorId, now), cancellationToken);
}

/// <summary>The demo outage as the dashboard's button needs it.</summary>
/// <param name="State">"none", "active" or "coolingDown".</param>
/// <param name="NextStartAt">When "Simulate an outage" works again, or null when it works now.</param>
public record DemoOutageDto(string State, DateTimeOffset? StartedAt, DateTimeOffset? ResolvesAt, DateTimeOffset? NextStartAt)
{
    public static DemoOutageDto From(DemoOutage? outage, DateTimeOffset now) => outage switch
    {
        null => new("none", null, null, null),
        _ when outage.IsActiveAt(now) => new("active", outage.StartedAt, outage.ResolvesAt, outage.CooldownEndsAt),
        _ when now < outage.CooldownEndsAt => new("coolingDown", outage.StartedAt, outage.ResolvesAt, outage.CooldownEndsAt),
        _ => new("none", null, null, null),
    };
}
