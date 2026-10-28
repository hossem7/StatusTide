extern alias Subscriber;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Common.Demo;
using StatusTide.Contracts.Polling;
using StatusTide.Contracts.Status;
using StatusTide.Data;
using StatusTide.Data.Incidents;
using StatusTide.Data.Seed;
using StatusTide.Data.Vendors;

using Subscriber::StatusTide.Subscriber.Features.Polling;
using Subscriber::StatusTide.Subscriber.Features.Polling.Demo;

namespace StatusTide.IntegrationTests.Demo;

/// <summary>
/// A simulated outage through the real poll worker, PostgreSQL and Redis: the outage becomes
/// an incident that is announced, and resolving it closes the incident.
/// </summary>
[Collection(DemoOutageCollection.Name)]
public sealed class DemoPollTests(PostgresFixture postgres, RedisFixture redis) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly string _database = $"demo_poll_{Guid.NewGuid():N}";
    private ServiceProvider _services = null!;
    private ITestHarness _harness = null!;

    private DemoOutageStore Store => _services.GetRequiredService<DemoOutageStore>();

    public async ValueTask InitializeAsync()
    {
        await (await postgres.CreateMigratedDbContextAsync(_database)).DisposeAsync();

        var db = redis.Connection.GetDatabase();
        await db.KeyDeleteAsync([
            DemoOutageStore.Key,
            VendorPollState.FingerprintKey(AcmeCloudSeed.VendorId),
            VendorPollState.ValidatorsKey(AcmeCloudSeed.VendorId),
            VendorPollState.FailuresKey(AcmeCloudSeed.VendorId),
        ]);

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(redis.Connection)
            .AddDbContext<StatusTideDbContext>(options => options
                .UseNpgsql(postgres.ConnectionString(_database))
                .UseSnakeCaseNamingConvention())
            .AddSingleton<DemoOutageStore>()
            .AddTransient<IStatusReader, DemoReader>()
            .AddSingleton<VendorPollState>()
            .AddScoped<SnapshotWriter>()
            .AddMassTransitTestHarness(bus => bus.AddConsumer<PollVendorConsumer, PollVendorConsumerDefinition>())
            .BuildServiceProvider(true);

        _harness = _services.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task A_quiet_demo_vendor_changes_nothing()
    {
        await PollAsync();

        Assert.Equal(0, ChangesAnnounced());
        await using var db = NewDbContext();
        Assert.False(await db.Incidents.AnyAsync(Cancel));
    }

    [Fact]
    public async Task A_simulated_outage_becomes_an_incident_and_resolving_closes_it()
    {
        // A real clock has more precision than PostgreSQL keeps.
        _time.Advance(TimeSpan.FromTicks(1_234_567));
        var (outage, started) = await Store.TryStartAsync();
        Assert.True(started);
        _time.Advance(TimeSpan.FromSeconds(2));
        await PollAsync();

        await using (var db = NewDbContext())
        {
            var api = await db.Components.SingleAsync(c => c.Id == AcmeCloudSeed.ApiComponentId, Cancel);
            Assert.Equal(ComponentStatus.MajorOutage, api.Status);

            var incident = await db.Incidents.Include(i => i.Components).SingleAsync(Cancel);
            Assert.Equal(IncidentStatus.Investigating, incident.Status);
            Assert.Equal(outage!.StartedAt, incident.StartedAt);
            Assert.Equal(
                [AcmeCloudSeed.ApiComponentId, AcmeCloudSeed.WebhooksComponentId],
                incident.Components.Select(c => c.Id).Order());
        }

        _time.Advance(TimeSpan.FromSeconds(30));
        var resolved = await Store.ResolveAsync();
        _time.Advance(TimeSpan.FromSeconds(2));
        await PollAsync();

        await using (var db = NewDbContext())
        {
            var incident = await db.Incidents.Include(i => i.Events).SingleAsync(Cancel);
            Assert.Equal(IncidentStatus.Resolved, incident.Status);
            Assert.Equal(resolved!.ResolvesAt, incident.ResolvedAt);
            Assert.Equal(
                [IncidentStatus.Investigating, IncidentStatus.Resolved],
                incident.Events.OrderBy(e => e.OccurredAt).Select(e => e.VendorStatus));
            Assert.All(
                await db.Components.Where(c => c.VendorId == AcmeCloudSeed.VendorId).ToListAsync(Cancel),
                c => Assert.Equal(ComponentStatus.Operational, c.Status));
        }

        Assert.Equal(2, ChangesAnnounced());
    }

    [Fact]
    public async Task An_outage_resolves_itself_after_its_duration()
    {
        await Store.TryStartAsync();
        await PollAsync();

        _time.Advance(DemoOutageStore.Duration + TimeSpan.FromSeconds(30));
        await PollAsync();

        await using var db = NewDbContext();
        var incident = await db.Incidents.SingleAsync(Cancel);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(Now + DemoOutageStore.Duration, incident.ResolvedAt);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Polls the demo vendor once and waits until the consumer has finished with it.</summary>
    private async Task PollAsync()
    {
        var correlationId = NewId.NextGuid();
        await _harness.Bus.Publish(new PollVendor(AcmeCloudSeed.VendorId, _time.GetUtcNow()), c => c.CorrelationId = correlationId, Cancel);

        var consumer = _harness.GetConsumerHarness<PollVendorConsumer>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        IReceivedMessage<PollVendor>? consumed;
        while ((consumed = consumer.Consumed.Select<PollVendor>(m => m.Context.CorrelationId == correlationId, Cancel).FirstOrDefault()) is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        Assert.Null(consumed.Exception);
    }

    private int ChangesAnnounced() =>
        _harness.Published.Select<VendorStatusChanged>(Cancel).Count(m => m.Context.Message.VendorId == AcmeCloudSeed.VendorId);

    private StatusTideDbContext NewDbContext() => new(new DbContextOptionsBuilder<StatusTideDbContext>()
        .UseNpgsql(postgres.ConnectionString(_database))
        .UseSnakeCaseNamingConvention()
        .Options);
}
