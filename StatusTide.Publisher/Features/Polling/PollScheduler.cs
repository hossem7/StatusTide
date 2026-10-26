using MassTransit;

using Microsoft.EntityFrameworkCore;

using StatusTide.Contracts.Polling;
using StatusTide.Data;

namespace StatusTide.Publisher.Features.Polling;

/// <summary>
/// Queues one <see cref="PollVendor"/> for every enabled vendor whose next poll is due,
/// then moves its next poll one interval ahead. However many users follow a vendor,
/// it is polled once per interval.
/// </summary>
public class PollScheduler(
    StatusTideDbContext db,
    IPublishEndpoint publish,
    TimeProvider timeProvider,
    ILogger<PollScheduler> logger)
{
    /// <summary>Upper bound per tick, so one tick stays short. The rest wait for the next tick.</summary>
    public const int BatchSize = 500;

    /// <summary>Polite polling: never more than once a minute, whatever a vendor row says.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>Returns how many polls were queued.</summary>
    public async Task<int> QueueDuePollsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await db.Vendors
            .Where(v => v.IsEnabled && (v.NextPollAt == null || v.NextPollAt <= now))
            .OrderBy(v => v.NextPollAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var vendor in due)
        {
            var interval = TimeSpan.FromSeconds(vendor.PollIntervalSeconds);
            vendor.NextPollAt = now + (interval < MinimumInterval ? MinimumInterval : interval);
        }

        // Saved before publishing, so a poll worker that backs off a failing vendor sets its
        // next poll after ours and is never overwritten. If publishing fails part way, the
        // rest wait one interval.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var vendor in due)
        {
            // A new id per poll, so one poll can be followed through every log line.
            var correlationId = NewId.NextGuid();
            await publish.Publish(
                new PollVendor(vendor.Id, now),
                context => context.CorrelationId = correlationId,
                cancellationToken);

            logger.LogInformation(
                "Queued poll for vendor {VendorId} with CorrelationId {CorrelationId}",
                vendor.Id,
                correlationId);
        }

        return due.Count;
    }
}
