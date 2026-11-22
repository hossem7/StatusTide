using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StatusTide.Api.Features.Catalog;
using StatusTide.Api.Features.CustomPages;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Follows;
using StatusTide.Contracts.CustomPages;
using StatusTide.Data;
using StatusTide.Data.Users;
using StatusTide.Data.Vendors;
using StatusTide.IntegrationTests.Dashboard;

namespace StatusTide.IntegrationTests.CustomPages;

/// <summary>
/// The custom page endpoints against real PostgreSQL. The Subscriber's answer comes from
/// <see cref="FakeDetectFeedConsumer"/>, so nothing is fetched.
/// </summary>
public sealed class CustomPagesTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly ApiFactory _factory;
    private readonly DashboardSeed _seed;

    public CustomPagesTests(PostgresFixture postgres, RedisFixture redis)
    {
        _factory = new ApiFactory(
            postgres,
            "custom_pages_api",
            services =>
            {
                services.AddSingleton(redis.Connection);
                services.AddTestAuth();
            },
            configureBus: bus => bus.AddConsumer<FakeDetectFeedConsumer>());
        _seed = new DashboardSeed(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_accounts_can_preview_or_add_pages()
    {
        var guest = await _seed.AddGuestAsync();
        using var guestClient = ClientFor(guest);
        using var nobody = _factory.CreateClient();
        var url = NewPageUrl();

        using var guestPreview = await guestClient.PostAsJsonAsync("/api/custom-pages/preview", new CustomPageRequest(url), Cancel);
        using var guestAdd = await guestClient.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(url), Cancel);
        using var anonymousAdd = await nobody.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(url), Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, guestPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guestAdd.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAdd.StatusCode);
        Assert.Null(await FindVendorAsync(url));
    }

    [Fact]
    public async Task A_preview_shows_the_components_and_saves_nothing()
    {
        using var client = ClientFor(await AddAccountAsync());
        var url = NewPageUrl();

        var preview = await PreviewAsync(client, url);

        Assert.Equal(url + "api/v2/summary.json", preview.FeedUrl);
        Assert.Equal(new Uri(url).Host, preview.Name);
        Assert.Equal(["API", "Webhooks"], preview.Components.Select(c => c.Name));
        Assert.Equal(ComponentStatus.DegradedPerformance, preview.Components[1].Status);
        Assert.Null(preview.CatalogVendorId);
        Assert.Null(preview.FollowingVendorId);
        Assert.Null(await FindVendorAsync(url));
    }

    [Fact]
    public async Task Adding_a_page_creates_a_custom_vendor_and_follows_all_of_it()
    {
        using var client = ClientFor(await AddAccountAsync());
        var url = NewPageUrl();

        var page = await AddAsync(client, url);

        Assert.All(page.Components, c => Assert.True(c.Followed));
        Assert.Equal(2, page.Components.Count);
        var vendor = (await FindVendorAsync(url))!;
        Assert.Equal(page.Id, vendor.Id);
        Assert.False(vendor.IsCatalog);
        Assert.True(vendor.IsEnabled);
        Assert.Null(vendor.Category);
        Assert.Null(vendor.NextPollAt);
        Assert.Equal(AddCustomPageCommand.PollIntervalSeconds, vendor.PollIntervalSeconds);
        Assert.Equal(FeedFormat.Statuspage, vendor.Format);
        Assert.StartsWith("custom-", vendor.Slug, StringComparison.Ordinal);

        var listed = await client.GetFromJsonAsync<CustomPagesDto>("/api/custom-pages", Json, Cancel);
        var mine = Assert.Single(listed!.Pages);
        Assert.Equal((vendor.Id, 2, 2), (mine.Id, mine.ComponentCount, mine.FollowedComponentCount));

        var preview = await PreviewAsync(client, url);
        Assert.Equal(vendor.Id, preview.FollowingVendorId);
    }

    [Fact]
    public async Task The_same_page_typed_differently_is_one_vendor()
    {
        using var first = ClientFor(await AddAccountAsync());
        using var second = ClientFor(await AddAccountAsync());
        var url = NewPageUrl();
        var host = new Uri(url).Host;

        var a = await AddAsync(first, url);
        var b = await AddAsync(second, $"  {host.ToUpperInvariant()}/#status");

        Assert.Equal(a.Id, b.Id);
        await using var db = CreateDb();
        Assert.Equal(1, await db.Vendors.CountAsync(v => v.FeedUrl.Contains(host), Cancel));
    }

    [Fact]
    public async Task Two_accounts_adding_a_new_page_at_once_share_one_vendor()
    {
        using var first = ClientFor(await AddAccountAsync());
        using var second = ClientFor(await AddAccountAsync());
        var url = NewPageUrl();

        var pages = await Task.WhenAll(AddAsync(first, url), AddAsync(second, url));

        Assert.Equal(pages[0].Id, pages[1].Id);
    }

    [Fact]
    public async Task An_account_can_add_at_most_ten_pages()
    {
        using var client = ClientFor(await AddAccountAsync());
        for (var i = 0; i < AddCustomPageCommand.CustomPageLimit; i++)
        {
            await AddAsync(client, NewPageUrl());
        }

        var url = NewPageUrl();
        using var response = await client.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(url), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("up to 10", await response.Content.ReadAsStringAsync(Cancel));
        Assert.Null(await FindVendorAsync(url));
    }

    [Fact]
    public async Task Custom_pages_count_toward_the_follow_limit()
    {
        var user = await AddAccountAsync();
        using var client = ClientFor(user);
        for (var i = 0; i < SetFollowsCommand.AccountVendorLimit; i++)
        {
            var vendor = await _seed.AddVendorAsync($"Limit{i}", ComponentStatus.Operational);
            await _seed.FollowAsync(user, vendor.Components[0]);
        }

        using var response = await client.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(NewPageUrl()), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("limit of services", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task A_page_in_the_catalog_is_followed_from_the_catalog()
    {
        using var client = ClientFor(await AddAccountAsync());
        var listed = await _seed.AddVendorAsync("Listed", ComponentStatus.Operational);
        await using (var db = CreateDb())
        {
            await db.Vendors.Where(v => v.Id == listed.Id).ExecuteUpdateAsync(v => v.SetProperty(x => x.IsEnabled, true), Cancel);
        }

        var pageUrl = new Uri(new Uri(listed.FeedUrl), "/").AbsoluteUri;
        var preview = await PreviewAsync(client, pageUrl);
        using var add = await client.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(pageUrl), Cancel);

        Assert.Equal(listed.Id, preview.CatalogVendorId);
        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        Assert.Contains(listed.Id.ToString(), await add.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task A_page_with_no_feed_is_refused_with_the_reason()
    {
        using var client = ClientFor(await AddAccountAsync());
        var url = NewPageUrl(FakeDetectFeedConsumer.NoFeedLabel);

        using var preview = await client.PostAsJsonAsync("/api/custom-pages/preview", new CustomPageRequest(url), Cancel);
        using var add = await client.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(url), Cancel);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, preview.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, add.StatusCode);
        var body = await add.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("notAFeed", body);
        Assert.Contains(FakeDetectFeedConsumer.NoFeedMessage, body);
        Assert.Null(await FindVendorAsync(url));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost/")]
    [InlineData("https://status.nimbusmail.example:8443/")]
    public async Task A_blocked_url_is_refused_before_asking_the_subscriber(string url)
    {
        using var client = ClientFor(await AddAccountAsync());
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var response = await client.PostAsJsonAsync("/api/custom-pages/preview", new CustomPageRequest(url), Cancel);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("blocked", await response.Content.ReadAsStringAsync(Cancel));
        Assert.False(await harness.Sent.Any<DetectFeed>(Cancel) || await harness.Published.Any<DetectFeed>(Cancel));
    }

    [Fact]
    public async Task Only_followers_can_see_or_change_a_page()
    {
        using var owner = ClientFor(await AddAccountAsync());
        using var other = ClientFor(await AddAccountAsync());
        var page = await AddAsync(owner, NewPageUrl());

        using var otherDetail = await other.GetAsync($"/api/catalog/{page.Id}", Cancel);
        using var otherFollow = await other.PutAsJsonAsync($"/api/follows/{page.Id}", new SetFollowsRequest(null), Cancel);
        using var ownerNarrow = await owner.PutAsJsonAsync($"/api/follows/{page.Id}", new SetFollowsRequest([page.Components[0].Id]), Cancel);

        Assert.Equal(HttpStatusCode.NotFound, otherDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherFollow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerNarrow.StatusCode);
        var narrowed = (await ownerNarrow.Content.ReadFromJsonAsync<CatalogVendorDetailDto>(Json, Cancel))!;
        Assert.Equal([true, false], narrowed.Components.Select(c => c.Followed));

        var catalog = await other.GetFromJsonAsync<CatalogDto>($"/api/catalog?search={page.Name}", Json, Cancel);
        Assert.Empty(catalog!.Vendors);
    }

    [Fact]
    public async Task A_page_nobody_follows_stops_polling_until_it_is_added_again()
    {
        using var first = ClientFor(await AddAccountAsync());
        using var second = ClientFor(await AddAccountAsync());
        var url = NewPageUrl();
        var page = await AddAsync(first, url);
        await AddAsync(second, url);
        await using (var db = CreateDb())
        {
            await db.Vendors.Where(v => v.Id == page.Id)
                .ExecuteUpdateAsync(v => v.SetProperty(x => x.NextPollAt, DateTimeOffset.UtcNow.AddMinutes(3)), Cancel);
        }

        (await first.DeleteAsync($"/api/follows/{page.Id}", Cancel)).Dispose();
        Assert.True((await FindVendorAsync(url))!.IsEnabled);

        (await second.DeleteAsync($"/api/follows/{page.Id}", Cancel)).Dispose();
        Assert.False((await FindVendorAsync(url))!.IsEnabled);

        var again = await AddAsync(first, url);
        var vendor = (await FindVendorAsync(url))!;
        Assert.Equal(page.Id, again.Id);
        Assert.True(vendor.IsEnabled);
        Assert.Null(vendor.NextPollAt);
    }

    [Fact]
    public async Task Unfollowing_a_catalog_vendor_never_turns_it_off()
    {
        var user = await AddAccountAsync();
        using var client = ClientFor(user);
        var listed = await _seed.AddVendorAsync("Kept", ComponentStatus.Operational);
        await using (var db = CreateDb())
        {
            await db.Vendors.Where(v => v.Id == listed.Id).ExecuteUpdateAsync(v => v.SetProperty(x => x.IsEnabled, true), Cancel);
        }

        await _seed.FollowAsync(user, listed.Components[0]);
        (await client.DeleteAsync($"/api/follows/{listed.Id}", Cancel)).Dispose();

        await using var check = CreateDb();
        Assert.True((await check.Vendors.SingleAsync(v => v.Id == listed.Id, Cancel)).IsEnabled);
    }

    [Fact]
    public async Task Previews_and_adds_are_rate_limited_per_user()
    {
        using var client = ClientFor(await AddAccountAsync());
        var url = NewPageUrl();
        for (var i = 0; i < CustomPagesExtensions.RequestsPerHour; i++)
        {
            await PreviewAsync(client, url);
        }

        using var response = await client.PostAsJsonAsync("/api/custom-pages/preview", new CustomPageRequest(url), Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static string NewPageUrl(string label = "status") => $"https://{label}.p{Guid.NewGuid():N}.example/";

    private static async Task<CustomPagePreviewDto> PreviewAsync(HttpClient client, string url)
    {
        using var response = await client.PostAsJsonAsync("/api/custom-pages/preview", new CustomPageRequest(url), Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomPagePreviewDto>(Json, Cancel))!;
    }

    private static async Task<CatalogVendorDetailDto> AddAsync(HttpClient client, string url)
    {
        using var response = await client.PostAsJsonAsync("/api/custom-pages", new CustomPageRequest(url), Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CatalogVendorDetailDto>(Json, Cancel))!;
    }

    private async Task<Vendor?> FindVendorAsync(string url)
    {
        var host = new Uri(url.Trim()).Host;
        await using var db = CreateDb();
        return await db.Vendors.AsNoTracking().SingleOrDefaultAsync(v => v.FeedUrl.Contains("//" + host + "/"), Cancel);
    }

    private async Task<Guid> AddAccountAsync()
    {
        var user = new User { CreatedAt = DashboardSeed.Now, LastSeenAt = DashboardSeed.Now };
        user.Email = $"{user.Id:N}@acme.example";
        await using var db = CreateDb();
        db.Users.Add(user);
        await db.SaveChangesAsync(Cancel);
        return user.Id;
    }

    private HttpClient ClientFor(Guid user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.Header, user.ToString());
        return client;
    }

    private StatusTideDbContext CreateDb()
    {
        // Starts the API first, which creates and migrates the database.
        _ = _factory.Services;
        return new(new DbContextOptionsBuilder<StatusTideDbContext>().UseNpgsql(_factory.ConnectionString).UseSnakeCaseNamingConvention().Options);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DashboardExtensions.ConfigureJson(json);
        return json;
    }
}

/// <summary>
/// Answers like the Subscriber would for a Statuspage site with two components, without
/// fetching anything. Hosts starting with <see cref="NoFeedLabel"/> have no feed.
/// </summary>
public sealed class FakeDetectFeedConsumer : IConsumer<DetectFeed>
{
    public const string NoFeedLabel = "nofeed";
    public const string NoFeedMessage = "No feed at this test page.";

    public Task Consume(ConsumeContext<DetectFeed> context)
    {
        var url = new Uri(context.Message.Url);
        if (url.Host.StartsWith(NoFeedLabel, StringComparison.Ordinal))
        {
            return context.RespondAsync(new FeedNotDetected(FeedNotDetectedReason.NotAFeed, NoFeedMessage));
        }

        var feedUrl = new Uri(url, "/api/v2/summary.json");
        return context.RespondAsync(new FeedDetected(
            feedUrl.AbsoluteUri,
            new Uri(url, "/").AbsoluteUri,
            nameof(FeedFormat.Statuspage),
            [
                new DetectedComponent("api", "API", nameof(ComponentStatus.Operational)),
                new DetectedComponent("webhooks", "Webhooks", nameof(ComponentStatus.DegradedPerformance)),
            ]));
    }
}
