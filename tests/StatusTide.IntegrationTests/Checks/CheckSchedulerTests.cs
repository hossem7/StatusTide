extern alias Publisher;

using MassTransit;
using MassTransit.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Publisher::StatusTide.Publisher.Features.Checks;

using StatusTide.Contracts.Checks;
using StatusTide.Data;
using StatusTide.Data.Seed;
using StatusTide.Data.Vendors;

namespace StatusTide.IntegrationTests.Checks;

public sealed class CheckSchedulerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider _services = null!;
    private ITestHarness _harness = null!;
    private StatusTideDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        _services = new ServiceCollection().AddMassTransitTestHarness().BuildServiceProvider(true);
        _harness = _services.GetRequiredService<ITestHarness>();
        await _harness.Start();

        _db = await postgres.CreateMigratedDbContextAsync($"check_scheduler_{Guid.NewGuid():N}");
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task Queues_one_check_for_each_enabled_vendor_with_a_check_url()
    {
        var withUrl = await AddVendorAsync("with-url", "https://api.with-url.example/health");
        var withoutUrl = await AddVendorAsync("without-url", null);
        var disabled = await AddVendorAsync("disabled", "https://api.disabled.example/health", isEnabled: false);

        var scheduler = new CheckScheduler(_db, _harness.Bus, new FakeTimeProvider(Now), NullLogger<CheckScheduler>.Instance);
        var queued = await scheduler.QueueChecksAsync(TestContext.Current.CancellationToken);

        var checks = _harness.Published.Select<CheckVendor>(TestContext.Current.CancellationToken).Select(m => m.Context.Message).ToList();
        Assert.Equal(2, queued);
        Assert.Equal([AcmeCloudSeed.VendorId, withUrl], checks.Select(c => c.VendorId).Order());
        Assert.All(checks, c => Assert.Equal(Now, c.ScheduledAt));
        Assert.DoesNotContain(withoutUrl, checks.Select(c => c.VendorId));
        Assert.DoesNotContain(disabled, checks.Select(c => c.VendorId));
    }

    private async Task<Guid> AddVendorAsync(string slug, string? checkUrl, bool isEnabled = true)
    {
        var vendor = new Vendor
        {
            Slug = slug,
            Name = slug,
            FeedUrl = $"https://status.{slug}.example/api/v2/summary.json",
            StatusPageUrl = $"https://status.{slug}.example",
            CheckUrl = checkUrl,
            Format = FeedFormat.Statuspage,
            IsEnabled = isEnabled,
            PollIntervalSeconds = 60,
            CreatedAt = Now,
        };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return vendor.Id;
    }
}
