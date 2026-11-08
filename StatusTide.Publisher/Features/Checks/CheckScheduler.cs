using MassTransit;

using Microsoft.EntityFrameworkCore;

using StatusTide.Contracts.Checks;
using StatusTide.Data;

namespace StatusTide.Publisher.Features.Checks;

/// <summary>
/// Queues one <see cref="CheckVendor"/> for every enabled vendor with a check URL. Runs once per
/// <see cref="Data.Checks.CheckPolicy.Interval"/>, so each vendor is checked once a minute.
/// </summary>
public class CheckScheduler(
    StatusTideDbContext db,
    IPublishEndpoint publish,
    TimeProvider timeProvider,
    ILogger<CheckScheduler> logger)
{
    /// <summary>Returns how many checks were queued.</summary>
    public async Task<int> QueueChecksAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var vendorIds = await db.Vendors
            .AsNoTracking()
            .Where(v => v.IsEnabled && v.CheckUrl != null)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        foreach (var vendorId in vendorIds)
        {
            var correlationId = NewId.NextGuid();
            await publish.Publish(
                new CheckVendor(vendorId, now),
                context => context.CorrelationId = correlationId,
                cancellationToken);
        }

        logger.LogInformation("Queued {Count} checks", vendorIds.Count);
        return vendorIds.Count;
    }
}
