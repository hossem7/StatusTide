extern alias Publisher;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Publisher::StatusTide.Publisher.Features.Checks;

using StatusTide.Data;
using StatusTide.Data.Checks;
using StatusTide.Data.Seed;

namespace StatusTide.IntegrationTests.Checks;

public sealed class CheckHousekeepingTests(PostgresFixture postgres) : IAsyncLifetime
{
    // 03:30 UTC, when the job runs.
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 3, 30, 0, TimeSpan.Zero);
    private static readonly Guid Vendor = AcmeCloudSeed.VendorId;

    private readonly FakeTimeProvider _time = new(Now);
    private StatusTideDbContext _db = null!;

    // A database per test, since the job works across the whole checks table.
    public async ValueTask InitializeAsync() => _db = await postgres.CreateMigratedDbContextAsync($"check_rollup_{Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rolls_up_each_finished_hour()
    {
        var hour = new DateTimeOffset(2026, 11, 9, 1, 0, 0, TimeSpan.Zero);
        await AddChecksAsync(
            (hour.AddMinutes(0), true, 100),
            (hour.AddMinutes(1), true, 300),
            (hour.AddMinutes(2), false, null),
            (hour.AddMinutes(59), false, 500));

        var (hours, _) = await CreateHousekeeping().RunAsync(Cancel);

        Assert.Equal(1, hours);
        var rollup = await _db.CheckRollups.SingleAsync(Cancel);
        Assert.Equal(hour, rollup.HourStart);
        Assert.Equal(4, rollup.TotalChecks);
        Assert.Equal(2, rollup.SuccessfulChecks);
        Assert.Equal(50.00m, rollup.UptimePercent);
        Assert.Equal(300, rollup.AverageResponseTimeMs);
    }

    [Fact]
    public async Task Leaves_the_current_hour_for_a_later_run()
    {
        await AddChecksAsync((Now.AddMinutes(-10), true, 100));

        await CreateHousekeeping().RunAsync(Cancel);

        Assert.Empty(await _db.CheckRollups.ToListAsync(Cancel));
        Assert.Single(await _db.Checks.ToListAsync(Cancel));
    }

    [Fact]
    public async Task Deletes_checks_older_than_7_days_in_whole_hours_after_rolling_them_up()
    {
        // The cutoff is 7 days ago rounded down to the hour: 2026-11-02 03:00.
        var cutoff = new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
        await AddChecksAsync(
            (cutoff.AddMinutes(-1), true, 100),
            (cutoff.AddMinutes(10), true, 100),
            (cutoff.AddMinutes(40), false, null));

        var (_, deleted) = await CreateHousekeeping().RunAsync(Cancel);

        // 03:10 and 03:40 are past the exact 7 days but stay, so their hour is never cut in half.
        Assert.Equal(1, deleted);
        Assert.Equal([cutoff.AddMinutes(10), cutoff.AddMinutes(40)], await _db.Checks.OrderBy(c => c.CheckedAt).Select(c => c.CheckedAt).ToListAsync(Cancel));

        var rollups = await _db.CheckRollups.OrderBy(r => r.HourStart).ToListAsync(Cancel);
        Assert.Equal([cutoff.AddHours(-1), cutoff], rollups.Select(r => r.HourStart));
        Assert.Equal(1, rollups[0].TotalChecks);
    }

    [Fact]
    public async Task Running_twice_gives_the_same_rollups()
    {
        var hour = new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero);
        await AddChecksAsync((hour.AddMinutes(5), true, 100), (hour.AddMinutes(6), false, 200));

        await CreateHousekeeping().RunAsync(Cancel);
        await CreateHousekeeping().RunAsync(Cancel);

        _db.ChangeTracker.Clear();
        var rollup = await _db.CheckRollups.SingleAsync(Cancel);
        Assert.Equal(2, rollup.TotalChecks);
        Assert.Equal(1, rollup.SuccessfulChecks);
        Assert.Equal(150, rollup.AverageResponseTimeMs);
    }

    [Fact]
    public async Task A_rollup_survives_after_its_raw_checks_are_deleted()
    {
        var hour = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        _db.CheckRollups.Add(new CheckRollup
        {
            VendorId = Vendor,
            HourStart = hour,
            TotalChecks = 60,
            SuccessfulChecks = 60,
            UptimePercent = 100m,
            AverageResponseTimeMs = 120,
        });
        await _db.SaveChangesAsync(Cancel);

        await CreateHousekeeping().RunAsync(Cancel);

        _db.ChangeTracker.Clear();
        Assert.Equal(60, (await _db.CheckRollups.SingleAsync(Cancel)).TotalChecks);
    }

    private CheckHousekeeping CreateHousekeeping() => new(_db, _time, NullLogger<CheckHousekeeping>.Instance);

    private async Task AddChecksAsync(params (DateTimeOffset At, bool Succeeded, int? ResponseTimeMs)[] checks)
    {
        foreach (var (at, succeeded, responseTime) in checks)
        {
            _db.Checks.Add(new Check
            {
                VendorId = Vendor,
                TargetUrl = AcmeCloudSeed.CheckUrl,
                CheckedAt = at,
                StatusCode = succeeded ? 200 : 503,
                ResponseTimeMs = responseTime,
                Succeeded = succeeded,
            });
        }

        await _db.SaveChangesAsync(Cancel);
        _db.ChangeTracker.Clear();
    }
}
