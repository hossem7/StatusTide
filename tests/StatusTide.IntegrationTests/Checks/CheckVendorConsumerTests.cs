extern alias Subscriber;

using System.Collections.Concurrent;
using System.Net;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Common.Demo;
using StatusTide.Contracts.Checks;
using StatusTide.Data;
using StatusTide.Data.Checks;
using StatusTide.Data.Seed;
using StatusTide.Data.Vendors;
using StatusTide.IntegrationTests.Demo;

using Subscriber::StatusTide.Subscriber.Features.Checks;

namespace StatusTide.IntegrationTests.Checks;

/// <summary>
/// The check worker against real PostgreSQL and Redis, with a fake health server. In the demo
/// outage collection, since one test simulates an Acme Cloud outage.
/// </summary>
[Collection(DemoOutageCollection.Name)]
public sealed class CheckVendorConsumerTests(PostgresFixture postgres, RedisFixture redis) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeHealthServer _server = new();
    private readonly string _database = $"checks_{Guid.NewGuid():N}";
    private ServiceProvider _services = null!;
    private ITestHarness _harness = null!;

    public async ValueTask InitializeAsync()
    {
        await (await postgres.CreateMigratedDbContextAsync(_database)).DisposeAsync();
        await redis.Connection.GetDatabase().KeyDeleteAsync(DemoOutageStore.Key);
        _server.Clock = _time;

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(redis.Connection)
            .AddSingleton<DemoOutageStore>()
            .AddDbContext<StatusTideDbContext>(options => options
                .UseNpgsql(postgres.ConnectionString(_database))
                .UseSnakeCaseNamingConvention())
            .AddScoped(sp => new HealthProbe(new HttpClient(_server), sp.GetRequiredService<DemoOutageStore>(), _time))
            .AddMassTransitTestHarness(bus => bus.AddConsumer<CheckVendorConsumer, CheckVendorConsumerDefinition>())
            .BuildServiceProvider(true);

        _harness = _services.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await redis.Connection.GetDatabase().KeyDeleteAsync(DemoOutageStore.Key);
    }

    [Fact]
    public async Task Saves_the_status_code_and_response_time()
    {
        var vendor = await AddVendorAsync("saved");
        _server.Answer(vendor, HttpStatusCode.OK, TimeSpan.FromMilliseconds(180));

        await CheckAsync(vendor);

        var check = Assert.Single(await ChecksAsync(vendor));
        Assert.Equal(200, check.StatusCode);
        Assert.Equal(180, check.ResponseTimeMs);
        Assert.True(check.Succeeded);
        Assert.Equal(Now, check.CheckedAt);
        Assert.Equal($"https://api.saved.example/health", check.TargetUrl);
    }

    [Fact]
    public async Task Three_failures_in_a_row_mark_the_vendor_down_once()
    {
        var vendor = await AddVendorAsync("down");
        _server.Answer(vendor, HttpStatusCode.ServiceUnavailable);

        await CheckAsync(vendor);
        await CheckAsync(vendor);
        Assert.Empty(await OutagesAsync(vendor));

        await CheckAsync(vendor);
        await CheckAsync(vendor);

        var outage = Assert.Single(await OutagesAsync(vendor));
        Assert.Equal(Now, outage.FirstFailedAt);
        Assert.Equal(Now.AddMinutes(2), outage.DetectedAt);
        Assert.Null(outage.ResolvedAt);
        Assert.Equal(4, (await ChecksAsync(vendor)).Count);
    }

    [Fact]
    public async Task A_success_between_failures_starts_the_count_again()
    {
        var vendor = await AddVendorAsync("flaky");

        foreach (var status in new[] { HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.OK, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway })
        {
            _server.Answer(vendor, status);
            await CheckAsync(vendor);
        }

        Assert.Empty(await OutagesAsync(vendor));
    }

    [Fact]
    public async Task A_timeout_counts_as_a_failure()
    {
        var vendor = await AddVendorAsync("timeout");
        _server.Unreachable(vendor);

        for (var i = 0; i < CheckPolicy.FailuresToMarkDown; i++)
        {
            await CheckAsync(vendor);
        }

        Assert.All(await ChecksAsync(vendor), c => Assert.Null(c.StatusCode));
        Assert.Single(await OutagesAsync(vendor));
    }

    [Fact]
    public async Task The_first_success_resolves_the_outage()
    {
        var vendor = await AddVendorAsync("recovers");
        _server.Answer(vendor, HttpStatusCode.ServiceUnavailable);
        for (var i = 0; i < CheckPolicy.FailuresToMarkDown; i++)
        {
            await CheckAsync(vendor);
        }

        _server.Answer(vendor, HttpStatusCode.OK);
        await CheckAsync(vendor);

        var outage = Assert.Single(await OutagesAsync(vendor));
        Assert.Equal(Now.AddMinutes(3), outage.ResolvedAt);

        // A later outage gets its own row.
        _server.Answer(vendor, HttpStatusCode.ServiceUnavailable);
        for (var i = 0; i < CheckPolicy.FailuresToMarkDown; i++)
        {
            await CheckAsync(vendor);
        }

        Assert.Equal(2, (await OutagesAsync(vendor)).Count);
    }

    [Fact]
    public async Task A_check_that_waited_longer_than_an_interval_is_skipped()
    {
        var vendor = await AddVendorAsync("stale");
        _server.Answer(vendor, HttpStatusCode.OK);

        await CheckAsync(vendor, scheduledAt: Now - CheckPolicy.Interval - TimeSpan.FromSeconds(1));

        Assert.Empty(await ChecksAsync(vendor));
        Assert.Equal(0, _server.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://api.bad-port.example:8443/health")]
    [InlineData("file:///etc/passwd")]
    public async Task Vendors_without_a_usable_check_url_are_not_checked(string? checkUrl)
    {
        var vendor = await AddVendorAsync($"no-check-{Guid.NewGuid():N}", checkUrl, setCheckUrl: true);

        await CheckAsync(vendor);

        Assert.Empty(await ChecksAsync(vendor));
        Assert.Equal(0, _server.Requests);
    }

    [Fact]
    public async Task Acme_cloud_fails_while_a_simulated_outage_runs()
    {
        await CheckAsync(AcmeCloudSeed.VendorId);

        await _services.GetRequiredService<DemoOutageStore>().TryStartAsync();
        for (var i = 0; i < CheckPolicy.FailuresToMarkDown; i++)
        {
            await CheckAsync(AcmeCloudSeed.VendorId);
        }

        var checks = await ChecksAsync(AcmeCloudSeed.VendorId);
        Assert.Equal([true, false, false, false], checks.Select(c => c.Succeeded));
        Assert.Equal(AcmeCloudSeed.CheckUrl, checks[0].TargetUrl);
        var outage = Assert.Single(await OutagesAsync(AcmeCloudSeed.VendorId));
        Assert.Equal(Now.AddMinutes(1), outage.FirstFailedAt);
        Assert.Equal(0, _server.Requests);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>
    /// Sends one check and waits until the consumer has finished with it, then moves time one
    /// interval ahead, as the scheduler would.
    /// </summary>
    private async Task CheckAsync(Guid vendorId, DateTimeOffset? scheduledAt = null)
    {
        var correlationId = NewId.NextGuid();
        await _harness.Bus.Publish(new CheckVendor(vendorId, scheduledAt ?? _time.GetUtcNow()), c => c.CorrelationId = correlationId, Cancel);

        // Waits for this one check; the harness's own Any() stops once the bus is idle.
        var consumer = _harness.GetConsumerHarness<CheckVendorConsumer>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        IReceivedMessage<CheckVendor>? consumed;
        while ((consumed = consumer.Consumed.Select<CheckVendor>(m => m.Context.CorrelationId == correlationId, Cancel).FirstOrDefault()) is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        Assert.Null(consumed.Exception);
        _time.Advance(CheckPolicy.Interval);
    }

    private StatusTideDbContext NewDbContext() => new(new DbContextOptionsBuilder<StatusTideDbContext>()
        .UseNpgsql(postgres.ConnectionString(_database))
        .UseSnakeCaseNamingConvention()
        .Options);

    private async Task<List<Check>> ChecksAsync(Guid vendorId)
    {
        await using var db = NewDbContext();
        return await db.Checks.Where(c => c.VendorId == vendorId).OrderBy(c => c.CheckedAt).ToListAsync(Cancel);
    }

    private async Task<List<CheckOutage>> OutagesAsync(Guid vendorId)
    {
        await using var db = NewDbContext();
        return await db.CheckOutages.Where(o => o.VendorId == vendorId).OrderBy(o => o.DetectedAt).ToListAsync(Cancel);
    }

    private async Task<Guid> AddVendorAsync(string slug, string? checkUrl = null, bool setCheckUrl = false)
    {
        await using var db = NewDbContext();
        var vendor = new Vendor
        {
            Slug = slug,
            Name = slug,
            FeedUrl = $"https://status.{slug}.example/api/v2/summary.json",
            StatusPageUrl = $"https://status.{slug}.example",
            CheckUrl = setCheckUrl ? checkUrl : $"https://api.{slug}.example/health",
            Format = FeedFormat.Statuspage,
            PollIntervalSeconds = 60,
            CreatedAt = Now,
        };

        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(Cancel);
        _server.Hosts[$"api.{slug}.example"] = vendor.Id;
        return vendor.Id;
    }

    /// <summary>Stands in for vendors' health URLs: answers each host the way its test asked.</summary>
    private sealed class FakeHealthServer : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<Guid, Func<Task<HttpResponseMessage>>> _answers = new();
        private int _requests;

        public ConcurrentDictionary<string, Guid> Hosts { get; } = new();

        public int Requests => _requests;

        /// <summary>Answers with <paramref name="status"/>. The delay is reported through the fake clock.</summary>
        public void Answer(Guid vendor, HttpStatusCode status, TimeSpan? delay = null) =>
            _answers[vendor] = () =>
            {
                if (delay is { } wait)
                {
                    Clock?.Advance(wait);
                }

                return Task.FromResult(new HttpResponseMessage(status));
            };

        public void Unreachable(Guid vendor) =>
            _answers[vendor] = () => throw new HttpRequestException("Connection refused");

        public FakeTimeProvider? Clock { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return _answers[Hosts[request.RequestUri!.Host]]();
        }
    }
}
