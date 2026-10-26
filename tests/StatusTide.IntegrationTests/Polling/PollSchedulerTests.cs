extern alias Publisher;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Publisher::StatusTide.Publisher.Features.Polling;

using StatusTide.Contracts.Polling;
using StatusTide.Data;
using StatusTide.Data.Seed;
using StatusTide.Data.Vendors;

namespace StatusTide.IntegrationTests.Polling;

public sealed class PollSchedulerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private ServiceProvider _services = null!;
    private ITestHarness _harness = null!;
    private StatusTideDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        // In-memory bus: these tests are about what gets queued, not about RabbitMQ.
        _services = new ServiceCollection().AddMassTransitTestHarness().BuildServiceProvider(true);
        _harness = _services.GetRequiredService<ITestHarness>();
        await _harness.Start();

        // A database per test, since every test changes vendors.
        _db = await postgres.CreateMigratedDbContextAsync($"scheduler_{Guid.NewGuid():N}");
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task Queues_one_poll_per_due_vendor()
    {
        var neverPolled = await AddVendorAsync("never-polled", nextPollAt: null);
        var overdue = await AddVendorAsync("overdue", nextPollAt: Now.AddSeconds(-5));
        var notDue = await AddVendorAsync("not-due", nextPollAt: Now.AddSeconds(30));
        var disabled = await AddVendorAsync("disabled", nextPollAt: null, isEnabled: false);

        var queued = await CreateScheduler().QueueDuePollsAsync(TestContext.Current.CancellationToken);

        // The seeded demo vendor has never been polled either.
        var polled = PublishedPolls().Select(p => p.VendorId).ToList();
        Assert.Equal(3, queued);
        Assert.Equal([AcmeCloudSeed.VendorId, neverPolled, overdue], polled.Order());
        Assert.DoesNotContain(notDue, polled);
        Assert.DoesNotContain(disabled, polled);
    }

    [Fact]
    public async Task Moves_next_poll_one_interval_ahead()
    {
        var vendor = await AddVendorAsync("vendor", nextPollAt: null, intervalSeconds: 300);

        await CreateScheduler().QueueDuePollsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddSeconds(300), await NextPollAtAsync(vendor));
    }

    [Fact]
    public async Task Never_polls_more_than_once_a_minute()
    {
        var vendor = await AddVendorAsync("too-eager", nextPollAt: null, intervalSeconds: 5);

        await CreateScheduler().QueueDuePollsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now + PollScheduler.MinimumInterval, await NextPollAtAsync(vendor));
    }

    [Fact]
    public async Task Queues_nothing_again_until_the_interval_passes()
    {
        await AddVendorAsync("vendor", nextPollAt: null, intervalSeconds: 60);
        var scheduler = CreateScheduler();
        var first = await scheduler.QueueDuePollsAsync(TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromSeconds(45));
        var second = await scheduler.QueueDuePollsAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(15));
        var third = await scheduler.QueueDuePollsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(2, third);
    }

    [Fact]
    public async Task Gives_every_poll_its_own_correlation_id()
    {
        await AddVendorAsync("first", nextPollAt: null);
        await AddVendorAsync("second", nextPollAt: null);

        await CreateScheduler().QueueDuePollsAsync(TestContext.Current.CancellationToken);

        var ids = _harness.Published.Select<PollVendor>(TestContext.Current.CancellationToken).Select(p => p.Context.CorrelationId).ToList();
        Assert.Equal(3, ids.Count);
        Assert.All(ids, id => Assert.NotNull(id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    private PollScheduler CreateScheduler() =>
        new(_db, _harness.Bus, _time, NullLogger<PollScheduler>.Instance);

    private List<PollVendor> PublishedPolls() =>
        _harness.Published.Select<PollVendor>(TestContext.Current.CancellationToken).Select(p => p.Context.Message).ToList();

    private async Task<Guid> AddVendorAsync(string slug, DateTimeOffset? nextPollAt, bool isEnabled = true, int intervalSeconds = 60)
    {
        var vendor = new Vendor
        {
            Slug = slug,
            Name = slug,
            FeedUrl = $"https://status.{slug}.example/api/v2/summary.json",
            StatusPageUrl = $"https://status.{slug}.example",
            Format = FeedFormat.Statuspage,
            IsEnabled = isEnabled,
            PollIntervalSeconds = intervalSeconds,
            NextPollAt = nextPollAt,
            CreatedAt = Now,
        };

        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return vendor.Id;
    }

    private Task<DateTimeOffset?> NextPollAtAsync(Guid vendorId) =>
        _db.Vendors.AsNoTracking()
            .Where(v => v.Id == vendorId)
            .Select(v => v.NextPollAt)
            .SingleAsync(TestContext.Current.CancellationToken);
}
