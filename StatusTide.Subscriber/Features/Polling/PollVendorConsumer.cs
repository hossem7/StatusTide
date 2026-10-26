using MassTransit;

using Microsoft.EntityFrameworkCore;

using StatusTide.Contracts.Polling;
using StatusTide.Contracts.Status;
using StatusTide.Data;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Polls one vendor. An unchanged feed stops here without writing anything; a changed one
/// is saved and announced with <see cref="VendorStatusChanged"/>. A feed that cannot be read
/// shows "unknown" and is polled less often until it recovers.
/// </summary>
public class PollVendorConsumer(
    StatusTideDbContext db,
    IEnumerable<IStatusReader> readers,
    VendorPollState state,
    SnapshotWriter writer,
    TimeProvider timeProvider,
    ILogger<PollVendorConsumer> logger) : IConsumer<PollVendor>
{
    /// <summary>Longest wait our own backoff adds between polls of a failing feed.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <summary>Longest Retry-After we honor, so a bad header cannot stop polling for weeks.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);

    public async Task Consume(ConsumeContext<PollVendor> context)
    {
        var cancellationToken = context.CancellationToken;
        var vendorId = context.Message.VendorId;

        var vendor = await db.Vendors
            .Include(v => v.Components)
            .SingleOrDefaultAsync(v => v.Id == vendorId, cancellationToken);
        if (vendor is not { IsEnabled: true })
        {
            logger.LogInformation("Skipping poll for vendor {VendorId}: missing or disabled", vendorId);
            return;
        }

        var reader = readers.FirstOrDefault(r => r.Format == vendor.Format);
        if (reader is null)
        {
            // Not an error: retrying would not help, and the demo vendor has no reader yet.
            logger.LogDebug("No reader for {Format} feeds; skipping vendor {VendorId}", vendor.Format, vendorId);
            return;
        }

        StatusReadResult result;
        try
        {
            var validators = await state.GetValidatorsAsync(vendorId);
            result = await reader.ReadAsync(new Uri(vendor.FeedUrl), validators, cancellationToken);
        }
        catch (StatusReadException e)
        {
            // Handled here instead of thrown, so the retry policy never hammers a struggling vendor.
            await HandleFailureAsync(context, vendor, e);
            return;
        }

        if (result.Snapshot is not { } snapshot)
        {
            logger.LogDebug("Feed for vendor {VendorId} not modified", vendorId);
            await state.RecordSuccessAsync(vendorId, null, result.Validators);
            return;
        }

        var fingerprint = SnapshotFingerprint.Of(snapshot);
        if (fingerprint == await state.GetFingerprintAsync(vendorId))
        {
            logger.LogDebug("Feed for vendor {VendorId} unchanged", vendorId);
            await state.RecordSuccessAsync(vendorId, null, result.Validators);
            return;
        }

        // The fingerprint is stored only after the database, so a failed save is retried in full.
        var now = timeProvider.GetUtcNow();
        if (await writer.SaveAsync(vendor, snapshot, now, cancellationToken))
        {
            logger.LogInformation("Status of vendor {VendorId} changed", vendorId);
            await PublishChangedAsync(context, vendorId, now);
        }

        await state.RecordSuccessAsync(vendorId, fingerprint, result.Validators);
    }

    private async Task HandleFailureAsync(ConsumeContext<PollVendor> context, Vendor vendor, StatusReadException error)
    {
        var now = timeProvider.GetUtcNow();
        var failures = await state.RecordFailureAsync(vendor.Id);
        var wait = Backoff(vendor, failures, error.RetryAfter);

        // Later than the scheduler's own next poll, which it set before queuing this one.
        vendor.NextPollAt = now + wait;
        var changed = SnapshotWriter.MarkUnknown(vendor, now);
        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogWarning(
            "Could not read feed for vendor {VendorId} ({Failure}, HTTP {StatusCode}), failure {Failures} in a row; next poll in {Wait}",
            vendor.Id,
            error.Failure,
            (int?)error.StatusCode,
            failures,
            wait);

        if (changed)
        {
            await PublishChangedAsync(context, vendor.Id, now);
        }
    }

    /// <summary>
    /// The poll interval doubled for each failure in a row, up to <see cref="MaxBackoff"/>,
    /// or the feed's Retry-After when that is longer.
    /// </summary>
    public static TimeSpan Backoff(Vendor vendor, long failures, TimeSpan? retryAfter)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(vendor.PollIntervalSeconds, 60));
        var doublings = Math.Min(failures - 1, 16);
        var backoff = TimeSpan.FromTicks(Math.Min(interval.Ticks << (int)doublings, MaxBackoff.Ticks));

        if (retryAfter is { } asked && asked > backoff)
        {
            return asked < MaxRetryAfter ? asked : MaxRetryAfter;
        }

        return backoff;
    }

    // Carries the poll's CorrelationId on to dashboards, alerts and summaries.
    private static Task PublishChangedAsync(ConsumeContext<PollVendor> context, Guid vendorId, DateTimeOffset now) =>
        context.Publish(new VendorStatusChanged(vendorId, now), publish => publish.CorrelationId = context.CorrelationId);
}

/// <summary>
/// Prefetch 1: each Subscriber takes one poll at a time, so a slow feed holds up only its own
/// replica and the rest of the queue stays free for others.
/// </summary>
public class PollVendorConsumerDefinition : ConsumerDefinition<PollVendorConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PollVendorConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            rabbit.PrefetchCount = 1;
        }
    }
}
