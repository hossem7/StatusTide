using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Guests;
using StatusTide.Api.Features.Users;
using StatusTide.Data;
using StatusTide.Data.Seed;

namespace StatusTide.IntegrationTests.Guests;

/// <summary>"Try it as a guest" through the real API and PostgreSQL, with a real cookie jar.</summary>
public sealed class GuestSessionTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly PostgresFixture _postgres;
    private readonly RedisFixture _redis;
    private readonly ApiFactory _factory;

    public GuestSessionTests(PostgresFixture postgres, RedisFixture redis)
    {
        _postgres = postgres;
        _redis = redis;
        _factory = new ApiFactory(postgres, "guests_api", services => services.AddSingleton(redis.Connection));
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_a_session_sets_a_secure_cookie_for_30_days()
    {
        using var client = CreateBrowser();

        using var response = await client.PostAsync("/api/guest", null, Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{GuestCookie.Name}=", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);

        var expires = DateTimeOffset.Parse(cookie.Split("expires=")[1].Split(';')[0]);
        var expected = DateTimeOffset.UtcNow.AddDays(30);
        Assert.InRange(expires, expected.AddMinutes(-1), expected.AddMinutes(1));
    }

    [Fact]
    public async Task The_database_keeps_only_a_hash_of_the_token()
    {
        using var client = CreateBrowser();
        var (me, token) = await StartAsync(client);

        await using var db = CreateDb();
        var guest = await db.Users.SingleAsync(u => u.Id == me.Id, Cancel);

        Assert.True(guest.IsGuest);
        Assert.Equal(32, guest.GuestTokenHash!.Length);
        Assert.Equal(GuestToken.Hash(token), guest.GuestTokenHash);
    }

    [Fact]
    public async Task A_new_guest_follows_the_demo_vendor_and_sees_it_on_the_dashboard()
    {
        using var client = CreateBrowser();
        var (me, _) = await StartAsync(client);

        Assert.True(me.IsGuest);
        Assert.Null(me.Email);
        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json, Cancel);
        var card = Assert.Single(dashboard!.Vendors);
        Assert.Equal(AcmeCloudSeed.VendorId, card.Id);
        Assert.Equal(card.ComponentCount, card.FollowedComponents.Count);
    }

    [Fact]
    public async Task The_cookie_signs_the_guest_in()
    {
        using var client = CreateBrowser();
        var (started, _) = await StartAsync(client);

        var me = await client.GetFromJsonAsync<MeDto>("/api/me", Json, Cancel);

        Assert.Equal(started.Id, me!.Id);
        Assert.NotNull(me.GuestExpiresAt);
    }

    [Fact]
    public async Task Starting_again_keeps_the_same_guest()
    {
        using var client = CreateBrowser();
        var (first, _) = await StartAsync(client);

        using var again = await client.PostAsync("/api/guest", null, Cancel);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first.Id, (await again.Content.ReadFromJsonAsync<MeDto>(Json, Cancel))!.Id);
        Assert.False(again.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // the right shape, but no guest has it
    public async Task An_unknown_cookie_is_not_signed_in(string token)
    {
        using var client = CreateClient(handleCookies: false);
        client.DefaultRequestHeaders.Add("Cookie", $"{GuestCookie.Name}={token}");

        using var response = await client.GetAsync("/api/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_cookie_me_is_unauthorized()
    {
        using var client = CreateBrowser();

        using var response = await client.GetAsync("/api/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_deleted_guest_is_signed_out_at_once()
    {
        using var client = CreateBrowser();
        var (me, _) = await StartAsync(client);
        await using (var db = CreateDb())
        {
            await db.Users.Where(u => u.Id == me.Id).ExecuteDeleteAsync(Cancel);
        }

        using var response = await client.GetAsync("/api/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_visit_after_an_hour_renews_the_last_seen_time_and_the_cookie()
    {
        using var client = CreateBrowser();
        var (me, token) = await StartAsync(client);
        await SetLastSeenAsync(me.Id, DateTimeOffset.UtcNow.AddHours(-2));

        using var response = await client.GetAsync("/api/me", Cancel);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{GuestCookie.Name}={token};", cookie);
        Assert.True(await LastSeenAsync(me.Id) > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task A_visit_within_the_hour_writes_nothing()
    {
        using var client = CreateBrowser();
        var (me, _) = await StartAsync(client);
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10);
        await SetLastSeenAsync(me.Id, tenMinutesAgo);

        using var response = await client.GetAsync("/api/me", Cancel);

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(tenMinutesAgo.ToUnixTimeSeconds(), (await LastSeenAsync(me.Id)).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task One_address_can_start_only_a_few_sessions_an_hour()
    {
        // A factory of its own, so this test's limit isn't shared with the other tests.
        await using var factory = new ApiFactory(_postgres, "guests_limit", services => services.AddSingleton(_redis.Connection));
        HttpClient Fresh() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        for (var i = 0; i < GuestExtensions.SessionsPerHour; i++)
        {
            using var browser = Fresh();
            using var allowed = await browser.PostAsync("/api/guest", null, Cancel);
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        using var client = Fresh();
        using var limited = await client.PostAsync("/api/guest", null, Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    /// <summary>A client that keeps cookies like a browser.</summary>
    private HttpClient CreateBrowser() => CreateClient(handleCookies: true);

    // HTTPS, since the cookie is Secure and a cookie jar won't send it over plain HTTP.
    private HttpClient CreateClient(bool handleCookies) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = handleCookies });

    /// <summary>Starts a session and returns it with the token from the Set-Cookie header.</summary>
    private static async Task<(MeDto Me, string Token)> StartAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/guest", null, Cancel);
        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var token = cookie[(GuestCookie.Name.Length + 1)..cookie.IndexOf(';')];
        return ((await response.Content.ReadFromJsonAsync<MeDto>(Json, Cancel))!, token);
    }

    private StatusTideDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<StatusTideDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new StatusTideDbContext(options);
    }

    private async Task SetLastSeenAsync(Guid id, DateTimeOffset lastSeen)
    {
        await using var db = CreateDb();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, lastSeen), Cancel);
    }

    private async Task<DateTimeOffset> LastSeenAsync(Guid id)
    {
        await using var db = CreateDb();
        return await db.Users.Where(u => u.Id == id).Select(u => u.LastSeenAt).SingleAsync(Cancel);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DashboardExtensions.ConfigureJson(json);
        return json;
    }
}
