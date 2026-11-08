using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Checks;

namespace StatusTide.Publisher.Features.Checks;

/// <summary>
/// Turns raw checks into one row per vendor per hour, then deletes raw checks older than
/// <see cref="CheckPolicy.RawRetention"/>. Safe to run twice: rollups are upserts.
/// </summary>
public class CheckHousekeeping(StatusTideDbContext db, TimeProvider timeProvider, ILogger<CheckHousekeeping> logger)
{
    public async Task<(int HoursRolledUp, int ChecksDeleted)> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Only finished hours. The current one is rolled up on a later night.
        var currentHour = StartOfHour(now);

        // Rounded down to a whole hour, so an hour's raw checks are either all kept or all
        // deleted. Otherwise the next run would roll up the leftover half of an hour and
        // overwrite its correct rollup with smaller numbers.
        var deleteBefore = StartOfHour(now - CheckPolicy.RawRetention);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Every finished hour that still has raw checks. Hours before deleteBefore are rolled
        // up here one last time if an earlier run was missed.
        var hours = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO check_rollups (vendor_id, hour_start, total_checks, successful_checks, uptime_percent, average_response_time_ms)
            SELECT vendor_id,
                   date_trunc('hour', checked_at, 'UTC'),
                   count(*),
                   count(*) FILTER (WHERE succeeded),
                   round(100.0 * count(*) FILTER (WHERE succeeded) / count(*), 2),
                   round(avg(response_time_ms))::integer
            FROM checks
            WHERE checked_at < {currentHour}
            GROUP BY vendor_id, date_trunc('hour', checked_at, 'UTC')
            ON CONFLICT (vendor_id, hour_start) DO UPDATE SET
                total_checks = excluded.total_checks,
                successful_checks = excluded.successful_checks,
                uptime_percent = excluded.uptime_percent,
                average_response_time_ms = excluded.average_response_time_ms
            """,
            cancellationToken);

        var deleted = await db.Checks
            .Where(c => c.CheckedAt < deleteBefore)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Rolled up {Hours} vendor hours of checks and deleted {Deleted} checks before {Cutoff}",
            hours,
            deleted,
            deleteBefore);
        return (hours, deleted);
    }

    private static DateTimeOffset StartOfHour(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
