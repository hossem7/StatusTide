extern alias Subscriber;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Contracts.Polling;
using StatusTide.Contracts.Status;
using StatusTide.Data;
using StatusTide.Data.Incidents;
using StatusTide.Data.Seed;
using StatusTide.Data.Vendors;

using Subscriber::StatusTide.Subscriber.Features.Polling;

namespace StatusTide.IntegrationTests.Polling;

/// <summary>
/// The poll worker against real PostgreSQL and Redis. The bus is in memory and the feeds are
/// fakes, so these tests are about what gets written and announced, not about HTTP.
/// </summary>
public sealed class PollVendorConsumerTests(PostgresFixture postgres, RedisFixture redis) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeReader _reader = new();
    private readonly WriteCounter _writes = new();
    private readonly string _database = $"poll_worker_{Guid.NewGuid():N}";
    private ServiceProvider _services = null!;
    private ITestHarness _harness = null!;

    public async ValueTask InitializeAsync()
    {
        await (await postgres.CreateMigratedDbContextAsync(_database)).DisposeAsync();

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(redis.Connection)
            .AddDbContext<StatusTideDbContext>(options => options
                .UseNpgsql(postgres.ConnectionString(_database))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(_writes))
            .AddSingleton<IStatusReader>(_reader)
            .AddSingleton<VendorPollState>()
            .AddScoped<SnapshotWriter>()
            .AddMassTransitTestHarness(bus => bus.AddConsumer<PollVendorConsumer, PollVendorConsumerDefinition>())
            .BuildServiceProvider(true);

        _harness = _services.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task First_poll_saves_components_and_incidents_and_announces_the_change()
    {
        var vendor = await AddVendorAsync("first-poll");
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.PartialOutage, Incident("inc-1", IncidentStatus.Identified));

        await PollAsync(vendor);

        await using var db = NewDbContext();
        var components = await db.Components.Where(c => c.VendorId == vendor).OrderBy(c => c.ExternalId).ToListAsync(Cancel);
        Assert.Equal(["api", "web"], components.Select(c => c.ExternalId));
        Assert.Equal(ComponentStatus.PartialOutage, components[0].Status);

        var incident = await db.Incidents.Include(i => i.Components).Include(i => i.Events).SingleAsync(i => i.VendorId == vendor, Cancel);
        Assert.Equal(IncidentStatus.Identified, incident.Status);
        Assert.Equal("api", Assert.Single(incident.Components).ExternalId);
        Assert.Equal([IncidentStatus.Investigating, IncidentStatus.Identified], incident.Events.OrderBy(e => e.OccurredAt).Select(e => e.VendorStatus));

        Assert.Equal(1, ChangesAnnounced(vendor));
    }

    [Fact]
    public async Task Polling_ten_unchanged_vendors_writes_nothing()
    {
        var vendors = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var vendor = await AddVendorAsync($"unchanged-{i}");
            _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational, Incident("inc-1", IncidentStatus.Monitoring));
            vendors.Add(vendor);
        }

        foreach (var vendor in vendors)
        {
            await PollAsync(vendor);
        }

        _writes.Reset();
        foreach (var vendor in vendors)
        {
            await PollAsync(vendor);
        }

        Assert.Empty(_writes.Commands);
        Assert.All(vendors, vendor => Assert.Equal(1, ChangesAnnounced(vendor)));
    }

    [Fact]
    public async Task Not_modified_writes_nothing_and_the_validators_are_sent_again()
    {
        var vendor = await AddVendorAsync("not-modified");
        var validators = new FeedValidators("\"v1\"", null);
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational);
        _reader.Validators = validators;
        await PollAsync(vendor);

        _writes.Reset();
        _reader.NotModified = true;
        await PollAsync(vendor);

        Assert.Equal(validators, _reader.ValidatorsSent[vendor].Last());
        Assert.Empty(_writes.Commands);
    }

    [Fact]
    public async Task Every_successful_poll_records_its_time_but_a_failed_one_does_not()
    {
        var vendor = await AddVendorAsync("poll-time");
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational);
        await PollAsync(vendor);
        Assert.Equal(Now.ToString("O"), await PolledAtAsync(vendor));

        // Unchanged polls write nothing to PostgreSQL, but still count as a check.
        _time.Advance(TimeSpan.FromMinutes(1));
        await PollAsync(vendor);
        Assert.Equal(Now.AddMinutes(1).ToString("O"), await PolledAtAsync(vendor));

        _time.Advance(TimeSpan.FromMinutes(1));
        _reader.Failure = new StatusReadException(StatusReadFailure.Timeout, "Timed out");
        await PollAsync(vendor);
        Assert.Equal(Now.AddMinutes(1).ToString("O"), await PolledAtAsync(vendor));
    }

    [Fact]
    public async Task A_status_change_is_saved_and_announced()
    {
        var vendor = await AddVendorAsync("status-change");
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational);
        await PollAsync(vendor);

        _time.Advance(TimeSpan.FromMinutes(1));
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.MajorOutage);
        await PollAsync(vendor);

        await using var db = NewDbContext();
        var api = await db.Components.SingleAsync(c => c.VendorId == vendor && c.ExternalId == "api", Cancel);
        Assert.Equal(ComponentStatus.MajorOutage, api.Status);
        Assert.Equal(Now.AddMinutes(1), api.StatusChangedAt);
        Assert.Equal(2, ChangesAnnounced(vendor));
    }

    [Fact]
    public async Task An_incident_that_leaves_the_feed_is_resolved()
    {
        var vendor = await AddVendorAsync("resolved");
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational, Incident("inc-1", IncidentStatus.Monitoring));
        await PollAsync(vendor);

        _time.Advance(TimeSpan.FromMinutes(1));
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational);
        await PollAsync(vendor);

        await using var db = NewDbContext();
        var incident = await db.Incidents.Include(i => i.Events).SingleAsync(i => i.VendorId == vendor, Cancel);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(Now.AddMinutes(1), incident.ResolvedAt);
        Assert.Equal(IncidentStatus.Resolved, incident.Events.MaxBy(e => e.OccurredAt)!.VendorStatus);
    }

    [Fact]
    public async Task A_failed_read_shows_unknown_backs_off_and_recovers()
    {
        var vendor = await AddVendorAsync("rate-limited");
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational);
        await PollAsync(vendor);

        _reader.Failure = new StatusReadException(StatusReadFailure.HttpError, "Too many requests")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            RetryAfter = TimeSpan.FromMinutes(10),
        };
        await PollAsync(vendor);

        await using (var db = NewDbContext())
        {
            Assert.All(await db.Components.Where(c => c.VendorId == vendor).ToListAsync(Cancel), c => Assert.Equal(ComponentStatus.Unknown, c.Status));
            Assert.Equal(Now.AddMinutes(10), (await db.Vendors.SingleAsync(v => v.Id == vendor, Cancel)).NextPollAt);
        }

        // The same feed as before the failure still counts as a change, since it replaces "unknown".
        _reader.Failure = null;
        await PollAsync(vendor);

        await using (var db = NewDbContext())
        {
            Assert.All(await db.Components.Where(c => c.VendorId == vendor).ToListAsync(Cancel), c => Assert.Equal(ComponentStatus.Operational, c.Status));
        }

        Assert.Null(_reader.ValidatorsSent[vendor].Last());
        Assert.Equal(3, ChangesAnnounced(vendor));
    }

    [Fact]
    public async Task Vendors_without_a_reader_are_skipped()
    {
        await PollAsync(AcmeCloudSeed.VendorId);

        Assert.False(_reader.ValidatorsSent.ContainsKey(AcmeCloudSeed.VendorId));
        Assert.Equal(0, ChangesAnnounced(AcmeCloudSeed.VendorId));
    }

    [Fact]
    public async Task The_change_event_carries_the_poll_correlation_id()
    {
        var vendor = await AddVendorAsync("correlation");
        _reader.Snapshots[vendor] = Snapshot(ComponentStatus.Operational);

        var correlationId = await PollAsync(vendor);

        var changed = _harness.Published.Select<VendorStatusChanged>(Cancel).Single(m => m.Context.Message.VendorId == vendor);
        Assert.Equal(correlationId, changed.Context.CorrelationId);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Sends one poll and waits until the consumer has finished with it.</summary>
    private async Task<Guid> PollAsync(Guid vendorId)
    {
        var correlationId = NewId.NextGuid();
        await _harness.Bus.Publish(new PollVendor(vendorId, _time.GetUtcNow()), c => c.CorrelationId = correlationId, Cancel);

        // Waits for this one poll. The harness's own Any() stops waiting once the bus has been
        // idle for a moment, which happens between the polls of one test.
        var consumer = _harness.GetConsumerHarness<PollVendorConsumer>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        IReceivedMessage<PollVendor>? consumed;
        while ((consumed = consumer.Consumed.Select<PollVendor>(m => m.Context.CorrelationId == correlationId, Cancel).FirstOrDefault()) is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        Assert.Null(consumed.Exception);
        return correlationId;
    }

    private async Task<string?> PolledAtAsync(Guid vendorId) =>
        await redis.Connection.GetDatabase().StringGetAsync(VendorPollState.PolledKey(vendorId));

    private int ChangesAnnounced(Guid vendorId) =>
        _harness.Published.Select<VendorStatusChanged>(Cancel).Count(m => m.Context.Message.VendorId == vendorId);

    /// <summary>A separate context for the test's own setup and checks, so they are not counted as writes.</summary>
    private StatusTideDbContext NewDbContext() => new(new DbContextOptionsBuilder<StatusTideDbContext>()
        .UseNpgsql(postgres.ConnectionString(_database))
        .UseSnakeCaseNamingConvention()
        .Options);

    private async Task<Guid> AddVendorAsync(string slug)
    {
        await using var db = NewDbContext();
        var vendor = new Vendor
        {
            Slug = slug,
            Name = slug,
            FeedUrl = $"https://status.{slug}.example/api/v2/summary.json",
            StatusPageUrl = $"https://status.{slug}.example",
            Format = FeedFormat.Statuspage,
            PollIntervalSeconds = 60,
            CreatedAt = Now,
        };

        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(Cancel);
        _reader.FeedUrls[new Uri(vendor.FeedUrl).AbsoluteUri] = vendor.Id;
        return vendor.Id;
    }

    private static StatusSnapshot Snapshot(ComponentStatus apiStatus, params IncidentSnapshot[] incidents) => new(
        [new("api", "API", apiStatus), new("web", "Website", ComponentStatus.Operational)],
        incidents);

    private static IncidentSnapshot Incident(string id, IncidentStatus status) => new(
        id,
        "Elevated API errors",
        status,
        Now.AddMinutes(-30),
        null,
        $"https://status.example/incidents/{id}",
        ["api"],
        [new(IncidentStatus.Investigating, Now.AddMinutes(-30)), new(status, Now.AddMinutes(-10))]);

    /// <summary>Stands in for a Statuspage feed: returns the snapshot set for each vendor's feed URL.</summary>
    private sealed class FakeReader : IStatusReader
    {
        /// <summary>Which vendor a feed URL belongs to, filled in as tests add vendors.</summary>
        public ConcurrentDictionary<string, Guid> FeedUrls { get; } = new();
        public ConcurrentDictionary<Guid, StatusSnapshot> Snapshots { get; } = new();
        public ConcurrentDictionary<Guid, List<FeedValidators?>> ValidatorsSent { get; } = new();
        public FeedValidators Validators { get; set; } = new(null, null);
        public bool NotModified { get; set; }
        public StatusReadException? Failure { get; set; }

        public FeedFormat Format => FeedFormat.Statuspage;

        public Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken)
        {
            var vendor = FeedUrls[feedUrl.AbsoluteUri];
            ValidatorsSent.GetOrAdd(vendor, _ => []).Add(validators);

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new StatusReadResult(NotModified ? null : Snapshots[vendor], Validators));
        }
    }

    /// <summary>Records every command that is not a SELECT, so a test can prove nothing was written.</summary>
    private sealed class WriteCounter : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; private set; } = new();

        public void Reset() => Commands = new ConcurrentQueue<string>();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Enqueue(command.CommandText);
            }
        }
    }
}
