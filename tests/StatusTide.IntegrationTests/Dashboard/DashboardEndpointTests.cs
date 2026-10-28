using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Data.Seed;
using StatusTide.Data.Vendors;

namespace StatusTide.IntegrationTests.Dashboard;

/// <summary>GET /api/dashboard through the real API, PostgreSQL and Redis.</summary>
public sealed class DashboardEndpointTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly RedisFixture _redis;
    private readonly ApiFactory _factory;
    private readonly DashboardSeed _seed;

    public DashboardEndpointTests(PostgresFixture postgres, RedisFixture redis)
    {
        _redis = redis;
        _factory = new ApiFactory(postgres, "dashboard_api", services => services
            .AddSingleton(redis.Connection)
            .AddTestAuth());
        _seed = new DashboardSeed(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Without_a_user_it_is_unauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/dashboard", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_user_who_follows_nothing_gets_no_vendors()
    {
        var user = await _seed.AddGuestAsync();

        var dashboard = await GetDashboardAsync(user);

        Assert.Empty(dashboard.Vendors);
    }

    [Fact]
    public async Task A_card_shows_only_followed_components_and_their_worst_status()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync(
            "Nimbus",
            ComponentStatus.Operational,
            ComponentStatus.PartialOutage,
            ComponentStatus.MajorOutage);
        await _seed.FollowAsync(user, vendor.Components[0], vendor.Components[1]);

        var card = Assert.Single((await GetDashboardAsync(user)).Vendors);

        Assert.Equal(vendor.Id, card.Id);
        Assert.Equal(ComponentStatus.PartialOutage, card.Status);
        Assert.Equal([vendor.Components[0].Id, vendor.Components[1].Id], card.FollowedComponents.Select(c => c.Id));
        Assert.Equal(3, card.ComponentCount);
        Assert.False(card.IsDemo);
    }

    [Fact]
    public async Task Statuses_are_sent_as_camel_case_strings()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync("Stratus", ComponentStatus.MajorOutage);
        await _seed.FollowAsync(user, vendor.Components[0]);

        using var client = ClientFor(user);
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/dashboard", Cancel));

        var card = body.RootElement.GetProperty("vendors")[0];
        Assert.Equal("majorOutage", card.GetProperty("status").GetString());
        Assert.Equal("majorOutage", card.GetProperty("followedComponents")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Vendors_without_followed_components_and_other_users_follows_are_left_out()
    {
        var user = await _seed.AddGuestAsync();
        var other = await _seed.AddGuestAsync();
        var mine = await _seed.AddVendorAsync("Cirrus", ComponentStatus.Operational);
        var theirs = await _seed.AddVendorAsync("Cumulus", ComponentStatus.MajorOutage);
        await _seed.AddVendorAsync("Nobody", ComponentStatus.Operational);
        await _seed.FollowAsync(user, mine.Components[0]);
        await _seed.FollowAsync(other, theirs.Components[0]);

        var card = Assert.Single((await GetDashboardAsync(user)).Vendors);

        Assert.Equal(mine.Id, card.Id);
    }

    [Fact]
    public async Task The_worst_vendor_comes_first()
    {
        var user = await _seed.AddGuestAsync();
        var fine = await _seed.AddVendorAsync("Alto", ComponentStatus.Operational);
        var down = await _seed.AddVendorAsync("Zephyr", ComponentStatus.MajorOutage);
        await _seed.FollowAsync(user, fine.Components[0]);
        await _seed.FollowAsync(user, down.Components[0]);

        var dashboard = await GetDashboardAsync(user);

        Assert.Equal([down.Id, fine.Id], dashboard.Vendors.Select(v => v.Id));
    }

    [Fact]
    public async Task A_card_counts_open_incidents_and_shows_the_latest()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync("Nimbus", ComponentStatus.PartialOutage);
        await _seed.FollowAsync(user, vendor.Components[0]);
        var now = DashboardSeed.Now;
        await _seed.AddIncidentAsync(vendor, "Older open incident", now.AddHours(-2));
        var latest = await _seed.AddIncidentAsync(vendor, "Newer open incident", now.AddMinutes(-10));
        await _seed.AddIncidentAsync(vendor, "Resolved incident", now.AddMinutes(-5), resolvedAt: now);

        var card = Assert.Single((await GetDashboardAsync(user)).Vendors);

        Assert.Equal(2, card.ActiveIncidentCount);
        Assert.Equal(new ActiveIncidentDto(latest.Id, latest.Title, latest.StartedAt), card.ActiveIncident);
    }

    [Fact]
    public async Task Last_checked_comes_from_the_poll_time_in_Redis()
    {
        var user = await _seed.AddGuestAsync();
        var polled = await _seed.AddVendorAsync("Polled", ComponentStatus.Operational);
        var neverPolled = await _seed.AddVendorAsync("NeverPolled", ComponentStatus.Operational);
        await _seed.FollowAsync(user, polled.Components[0], neverPolled.Components[0]);
        var polledAt = DashboardSeed.Now.AddSeconds(-40);
        await _redis.Connection.GetDatabase().StringSetAsync(DashboardQuery.PolledKey(polled.Id), polledAt.ToString("O"), TimeSpan.FromMinutes(5));

        var vendors = (await GetDashboardAsync(user)).Vendors.ToDictionary(v => v.Id);

        Assert.Equal(polledAt, vendors[polled.Id].LastCheckedAt);
        Assert.Null(vendors[neverPolled.Id].LastCheckedAt);
    }

    [Fact]
    public async Task The_demo_vendor_is_marked_as_the_demo()
    {
        var user = await _seed.AddGuestAsync();
        await _seed.FollowAsync(user, AcmeCloudSeed.ApiComponentId);

        var card = Assert.Single((await GetDashboardAsync(user)).Vendors);

        Assert.Equal("Acme Cloud (demo)", card.Name);
        Assert.True(card.IsDemo);
        Assert.Equal(4, card.ComponentCount);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.Header, userId.ToString());
        return client;
    }

    private async Task<DashboardDto> GetDashboardAsync(Guid userId)
    {
        using var client = ClientFor(userId);
        return (await client.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json, Cancel))!;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DashboardExtensions.ConfigureJson(options);
        return options;
    }
}
