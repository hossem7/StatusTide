using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Incidents;
using StatusTide.Data;
using StatusTide.Data.Checks;
using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.IntegrationTests.Dashboard;

namespace StatusTide.IntegrationTests.Incidents;

/// <summary>GET /api/incidents/{id} through the real API and PostgreSQL, on a fake clock.</summary>
public sealed class IncidentEndpointTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = DashboardSeed.Now;
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly ApiFactory _factory;
    private readonly DashboardSeed _seed;

    public IncidentEndpointTests(PostgresFixture postgres, RedisFixture redis)
    {
        _factory = new ApiFactory(postgres, "incident_api", services => services
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Now))
            .AddSingleton(redis.Connection)
            .AddTestAuth());
        _seed = new DashboardSeed(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Without_a_user_it_is_unauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/incidents/{Guid.NewGuid()}", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_incident_is_not_found()
    {
        var user = await _seed.AddGuestAsync();
        using var client = ClientFor(user);

        using var response = await client.GetAsync($"/api/incidents/{Guid.NewGuid()}", Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_incident_on_a_custom_page_shows_only_to_its_followers()
    {
        var follower = await _seed.AddGuestAsync();
        var stranger = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync("Private", ComponentStatus.MajorOutage);
        await SaveAsync(db =>
        {
            db.Vendors.Attach(vendor);
            vendor.IsCatalog = false;
        });
        await _seed.FollowAsync(follower, vendor.Components[0]);
        var incident = await _seed.AddIncidentAsync(vendor, "Login errors", Now.AddMinutes(-5), null, vendor.Components[0]);

        using var strangerClient = ClientFor(stranger);
        using var hidden = await strangerClient.GetAsync($"/api/incidents/{incident.Id}", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        Assert.Equal(incident.Id, (await GetIncidentAsync(follower, incident.Id)).Id);
    }

    [Fact]
    public async Task The_page_holds_the_facts_affected_and_followed_components_and_updates()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync(
            "Nimbus",
            ComponentStatus.MajorOutage,
            ComponentStatus.Operational,
            ComponentStatus.Operational);
        await _seed.FollowAsync(user, vendor.Components[1]);
        var incident = await _seed.AddIncidentAsync(vendor, "API errors", Now.AddMinutes(-20), null, vendor.Components[0]);
        await AddUpdatesAsync(incident, (IncidentStatus.Investigating, Now.AddMinutes(-20)), (IncidentStatus.Identified, Now.AddMinutes(-8)));

        var page = await GetIncidentAsync(user, incident.Id);

        Assert.Equal("API errors", page.Title);
        Assert.Equal(IncidentStatus.Investigating, page.Status);
        Assert.Equal(incident.SourceUrl, page.SourceUrl);
        Assert.Equal(vendor.Id, page.Vendor.Id);
        Assert.False(page.Vendor.IsDemo);
        Assert.Equal(
            [(vendor.Components[0].Id, true, false), (vendor.Components[1].Id, false, true)],
            page.Components.Select(c => (c.Id, c.IsAffected, c.IsFollowed)));
        Assert.Equal([IncidentStatus.Identified, IncidentStatus.Investigating], page.Updates.Select(u => u.Status));
        Assert.Null(page.Checks); // The seeded vendor has no check URL.
    }

    [Fact]
    public async Task Our_checks_caught_it_before_the_vendor_posted()
    {
        var user = await _seed.AddGuestAsync();
        var (vendor, incident) = await AddCheckedIncidentAsync(postedAt: Now.AddMinutes(-10));

        // Down 6 minutes before the post, confirmed at the 3rd failure, 4 minutes before it.
        await AddChecksAsync(vendor, Now.AddMinutes(-30), passing: 14, failing: 17);
        await AddOutageAsync(vendor, firstFailedAt: Now.AddMinutes(-16), detectedAt: Now.AddMinutes(-14), resolvedAt: null);

        var checks = (await GetIncidentAsync(user, incident.Id)).Checks!;

        var outage = checks.Outage!;
        Assert.Equal(Now.AddMinutes(-16), outage.FirstFailedAt);
        Assert.Equal(Now.AddMinutes(-14), outage.DetectedAt);
        Assert.Equal(Now.AddMinutes(-10), outage.VendorPostedAt);
        Assert.Equal(240, outage.LeadSeconds);

        // From 10 minutes before our first failure up to now, while the incident is open.
        Assert.Equal(Now.AddMinutes(-26), checks.From);
        Assert.Equal(Now, checks.To);
        Assert.Equal(27, checks.Points.Count);
        Assert.Equal(Now.AddMinutes(-26), checks.Points[0].CheckedAt);
        Assert.Equal(10, checks.Points.Count(p => p.Succeeded));
        Assert.All(checks.Points.Where(p => !p.Succeeded), p => Assert.Equal(503, p.StatusCode));
    }

    [Fact]
    public async Task A_vendor_that_posted_first_gets_a_negative_lead()
    {
        var user = await _seed.AddGuestAsync();
        var (vendor, incident) = await AddCheckedIncidentAsync(postedAt: Now.AddMinutes(-10));
        await AddOutageAsync(vendor, firstFailedAt: Now.AddMinutes(-9), detectedAt: Now.AddMinutes(-7), resolvedAt: Now.AddMinutes(-2));

        var checks = (await GetIncidentAsync(user, incident.Id)).Checks!;

        Assert.Equal(-180, checks.Outage!.LeadSeconds);
        Assert.Equal(Now.AddMinutes(-2), checks.Outage.RecoveredAt);
        Assert.Equal(Now.AddMinutes(-20), checks.From);
    }

    [Fact]
    public async Task An_outage_that_ended_long_before_the_post_is_not_the_same_problem()
    {
        var user = await _seed.AddGuestAsync();
        var (vendor, incident) = await AddCheckedIncidentAsync(postedAt: Now.AddMinutes(-10));
        await AddOutageAsync(vendor, firstFailedAt: Now.AddHours(-5), detectedAt: Now.AddHours(-5).AddMinutes(2), resolvedAt: Now.AddHours(-4));

        var checks = (await GetIncidentAsync(user, incident.Id)).Checks!;

        Assert.Null(checks.Outage);
        Assert.Empty(checks.Points);
        Assert.Equal(Now.AddMinutes(-20), checks.From);
    }

    [Fact]
    public async Task A_long_incident_charts_only_its_first_hours()
    {
        var user = await _seed.AddGuestAsync();
        var (_, incident) = await AddCheckedIncidentAsync(postedAt: Now.AddHours(-10));

        var checks = (await GetIncidentAsync(user, incident.Id)).Checks!;

        Assert.Equal(IncidentQuery.ChartLength, checks.To - checks.From);
    }

    private async Task<(Vendor Vendor, Incident Incident)> AddCheckedIncidentAsync(DateTimeOffset postedAt)
    {
        var vendor = await _seed.AddVendorAsync("Checked", ComponentStatus.MajorOutage);
        await SaveAsync(db =>
        {
            db.Vendors.Attach(vendor);
            vendor.CheckUrl = $"https://{vendor.Slug}.example/health";
        });
        var incident = await _seed.AddIncidentAsync(vendor, "Outage", postedAt, null, vendor.Components[0]);
        await AddUpdatesAsync(incident, (IncidentStatus.Investigating, postedAt));
        return (vendor, incident);
    }

    private Task AddUpdatesAsync(Incident incident, params (IncidentStatus Status, DateTimeOffset At)[] updates) => SaveAsync(db =>
        db.IncidentEvents.AddRange(updates.Select(u => new IncidentEvent
        {
            IncidentId = incident.Id,
            Kind = IncidentEventKind.VendorUpdate,
            VendorStatus = u.Status,
            OccurredAt = u.At,
        })));

    /// <summary>One check a minute from <paramref name="from"/>: passing ones, then failing ones.</summary>
    private Task AddChecksAsync(Vendor vendor, DateTimeOffset from, int passing, int failing) => SaveAsync(db =>
        db.Checks.AddRange(Enumerable.Range(0, passing + failing).Select(i => new Check
        {
            VendorId = vendor.Id,
            TargetUrl = vendor.CheckUrl!,
            CheckedAt = from.AddMinutes(i),
            Succeeded = i < passing,
            StatusCode = i < passing ? 200 : 503,
            ResponseTimeMs = i < passing ? 120 : 40,
        })));

    private Task AddOutageAsync(Vendor vendor, DateTimeOffset firstFailedAt, DateTimeOffset detectedAt, DateTimeOffset? resolvedAt) =>
        SaveAsync(db => db.CheckOutages.Add(new CheckOutage
        {
            VendorId = vendor.Id,
            FirstFailedAt = firstFailedAt,
            DetectedAt = detectedAt,
            ResolvedAt = resolvedAt,
        }));

    private async Task SaveAsync(Action<StatusTideDbContext> change)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();
        change(db);
        await db.SaveChangesAsync(Cancel);
    }

    private HttpClient ClientFor(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.Header, userId.ToString());
        return client;
    }

    private async Task<IncidentDto> GetIncidentAsync(Guid userId, Guid incidentId)
    {
        using var client = ClientFor(userId);
        return (await client.GetFromJsonAsync<IncidentDto>($"/api/incidents/{incidentId}", Json, Cancel))!;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DashboardExtensions.ConfigureJson(options);
        return options;
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
}
