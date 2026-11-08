using MassTransit;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using StatusTide.Contracts.Checks;
using StatusTide.Data;
using StatusTide.Data.Checks;

namespace StatusTide.Subscriber.Features.Checks;

/// <summary>
/// Runs one of our own checks and saves the result. After
/// <see cref="CheckPolicy.FailuresToMarkDown"/> failures in a row the vendor is marked down with
/// a <see cref="CheckOutage"/>; the next success closes it.
/// </summary>
public class CheckVendorConsumer(
    StatusTideDbContext db,
    HealthProbe probe,
    TimeProvider timeProvider,
    ILogger<CheckVendorConsumer> logger) : IConsumer<CheckVendor>
{
    public async Task Consume(ConsumeContext<CheckVendor> context)
    {
        var cancellationToken = context.CancellationToken;
        var vendorId = context.Message.VendorId;

        // A check that waited a whole interval in the queue (the Subscriber was down, or the
        // queue backed up) is skipped: a newer one is already queued, and running both would
        // count one moment twice towards the failure streak.
        var waited = timeProvider.GetUtcNow() - context.Message.ScheduledAt;
        if (waited > CheckPolicy.Interval)
        {
            logger.LogDebug("Skipping check for vendor {VendorId}: queued {Waited} ago", vendorId, waited);
            return;
        }

        var vendor = await db.Vendors.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vendorId, cancellationToken);
        if (vendor is not { IsEnabled: true, CheckUrl: { } checkUrl })
        {
            logger.LogInformation("Skipping check for vendor {VendorId}: missing, disabled or no check URL", vendorId);
            return;
        }

        if (!Uri.TryCreate(checkUrl, UriKind.Absolute, out var url) || !HealthProbe.IsSupported(url))
        {
            // Not retried: the vendor row has to be fixed first.
            logger.LogError("Check URL of vendor {VendorId} is not an http, https or demo URL on a default port", vendorId);
            return;
        }

        var checkedAt = timeProvider.GetUtcNow();
        var result = await probe.ProbeAsync(url, cancellationToken);

        // The check and any outage change are saved together, so a retry after a failure
        // starts from a clean state instead of counting the same check twice.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Checks.Add(new Check
        {
            VendorId = vendorId,
            TargetUrl = checkUrl,
            CheckedAt = checkedAt,
            StatusCode = result.StatusCode,
            ResponseTimeMs = result.ResponseTimeMs,
            Succeeded = result.Succeeded,
        });
        await db.SaveChangesAsync(cancellationToken);

        if (result.Succeeded)
        {
            await ResolveOutageAsync(vendorId, checkedAt, cancellationToken);
        }
        else
        {
            await OpenOutageIfDownAsync(vendorId, result, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ResolveOutageAsync(Guid vendorId, DateTimeOffset checkedAt, CancellationToken cancellationToken)
    {
        var resolved = await db.CheckOutages
            .Where(o => o.VendorId == vendorId && o.ResolvedAt == null)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.ResolvedAt, checkedAt), cancellationToken);

        if (resolved > 0)
        {
            logger.LogInformation("Our checks see vendor {VendorId} up again", vendorId);
        }
    }

    private async Task OpenOutageIfDownAsync(Guid vendorId, ProbeResult result, CancellationToken cancellationToken)
    {
        // The latest checks, including the one just saved.
        var latest = await db.Checks
            .AsNoTracking()
            .Where(c => c.VendorId == vendorId)
            .OrderByDescending(c => c.CheckedAt)
            .Take(CheckPolicy.FailuresToMarkDown)
            .Select(c => new { c.CheckedAt, c.Succeeded })
            .ToListAsync(cancellationToken);

        if (latest.Count < CheckPolicy.FailuresToMarkDown || latest.Any(c => c.Succeeded))
        {
            return;
        }

        if (await db.CheckOutages.AnyAsync(o => o.VendorId == vendorId && o.ResolvedAt == null, cancellationToken))
        {
            return;
        }

        db.CheckOutages.Add(new CheckOutage
        {
            VendorId = vendorId,
            FirstFailedAt = latest[^1].CheckedAt,
            DetectedAt = latest[0].CheckedAt,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another worker opened it a moment ago. The retry finds it open and stops.
            throw new InvalidOperationException($"An outage for vendor {vendorId} was opened by another check; retrying.", e);
        }

        logger.LogWarning(
            "Our checks mark vendor {VendorId} down after {Failures} failures in a row (last HTTP {StatusCode})",
            vendorId,
            CheckPolicy.FailuresToMarkDown,
            result.StatusCode);
    }
}

/// <summary>
/// Several checks at once per Subscriber: a vendor that times out holds one slot for 10 seconds,
/// and the rest of the minute's checks should not wait behind it.
/// </summary>
public class CheckVendorConsumerDefinition : ConsumerDefinition<CheckVendorConsumer>
{
    public const int ConcurrentChecks = 8;

    public CheckVendorConsumerDefinition() => ConcurrentMessageLimit = ConcurrentChecks;
}
