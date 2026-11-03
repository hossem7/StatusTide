using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StatusTide.Api.Features.Catalog;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Follows;
using StatusTide.Data;
using StatusTide.Data.Users;
using StatusTide.Data.Vendors;
using StatusTide.IntegrationTests.Dashboard;

namespace StatusTide.IntegrationTests.Catalog;

/// <summary>The catalog and follow endpoints against real PostgreSQL, with made-up vendors.</summary>
public sealed class CatalogAndFollowsTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly ApiFactory _factory;
    private readonly DashboardSeed _seed;

    public CatalogAndFollowsTests(PostgresFixture postgres, RedisFixture redis)
    {
        _factory = new ApiFactory(postgres, "catalog_api", services =>
        {
            services.AddSingleton(redis.Connection);
            services.AddTestAuth();
        });
        _seed = new DashboardSeed(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalog_lists_only_enabled_catalog_vendors()
    {
        var user = await _seed.AddGuestAsync();
        var word = Unique();
        var listed = await AddListedVendorAsync($"{word} Listed");
        await _seed.AddVendorAsync($"{word}Disabled", ComponentStatus.Operational);
        var custom = await AddListedVendorAsync($"{word} Custom");
        await ChangeVendorAsync(custom.Id, v => v.SetProperty(x => x.IsCatalog, false));

        var catalog = await GetCatalogAsync(user, $"search={word}");

        Assert.Equal(listed.Id, Assert.Single(catalog.Vendors).Id);
    }

    [Fact]
    public async Task The_demo_vendor_is_in_the_catalog_under_cloud()
    {
        var user = await _seed.AddGuestAsync();

        var catalog = await GetCatalogAsync(user, "search=acme");

        var acme = Assert.Single(catalog.Vendors);
        Assert.Equal(VendorCategory.Cloud, acme.Category);
        Assert.Equal(4, acme.ComponentCount);
    }

    [Fact]
    public async Task Search_ignores_case_and_treats_wildcards_as_plain_text()
    {
        var user = await _seed.AddGuestAsync();
        var word = Unique();
        var plain = await AddListedVendorAsync($"{word} Plain");
        var percent = await AddListedVendorAsync($"{word} 100% up");

        Assert.Equal(2, (await GetCatalogAsync(user, $"search={word.ToUpperInvariant()}")).Vendors.Count);
        Assert.Equal(percent.Id, Assert.Single((await GetCatalogAsync(user, $"search={word}%20100%25")).Vendors).Id);
        Assert.Empty((await GetCatalogAsync(user, $"search={word}%25Plain")).Vendors);
        Assert.Contains((await GetCatalogAsync(user, $"search={word}")).Vendors, v => v.Id == plain.Id);
    }

    [Fact]
    public async Task The_category_filter_narrows_the_list()
    {
        var user = await _seed.AddGuestAsync();
        var word = Unique();
        var payments = await AddListedVendorAsync($"{word} Pay", VendorCategory.Payments);
        await AddListedVendorAsync($"{word} Chat", VendorCategory.Communication);

        var catalog = await GetCatalogAsync(user, $"search={word}&category=payments");

        Assert.Equal(payments.Id, Assert.Single(catalog.Vendors).Id);
    }

    [Theory]
    [InlineData("category=notACategory")]
    public async Task A_bad_filter_is_refused(string query)
    {
        var user = await _seed.AddGuestAsync();
        using var client = ClientFor(user);

        using var response = await client.GetAsync($"/api/catalog?{query}", Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_overlong_search_is_refused()
    {
        var user = await _seed.AddGuestAsync();
        using var client = ClientFor(user);

        using var response = await client.GetAsync($"/api/catalog?search={new string('a', 101)}", Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Every_endpoint_needs_a_session()
    {
        var vendor = await AddListedVendorAsync(Unique());
        using var client = _factory.CreateClient();

        using var list = await client.GetAsync("/api/catalog", Cancel);
        using var detail = await client.GetAsync($"/api/catalog/{vendor.Id}", Cancel);
        using var follow = await client.PutAsJsonAsync($"/api/follows/{vendor.Id}", new SetFollowsRequest(null), Cancel);
        using var unfollow = await client.DeleteAsync($"/api/follows/{vendor.Id}", Cancel);

        Assert.All([list, detail, follow, unfollow], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    [Fact]
    public async Task Following_with_no_list_follows_every_component_and_shows_on_the_dashboard()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);

        var detail = await FollowAsync(client, vendor.Id, null);

        Assert.All(detail.Components, c => Assert.True(c.Followed));
        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json, Cancel);
        Assert.Equal(2, Assert.Single(dashboard!.Vendors).FollowedComponents.Count);
    }

    [Fact]
    public async Task Choosing_components_follows_only_those()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);

        var detail = await FollowAsync(client, vendor.Id, [vendor.Components[1].Id]);

        Assert.Equal(vendor.Components[1].Id, Assert.Single(detail.Components, c => c.Followed).Id);
        var row = Assert.Single((await GetCatalogAsync(user, $"search={vendor.Name}")).Vendors);
        Assert.Equal(1, row.FollowedComponentCount);
        Assert.Equal(2, row.ComponentCount);
    }

    [Fact]
    public async Task Changing_components_keeps_the_alert_settings_of_the_ones_still_chosen()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);
        await FollowAsync(client, vendor.Id, null);
        var kept = vendor.Components[0].Id;
        await using (var db = CreateDb())
        {
            await db.Follows
                .Where(f => f.UserId == user && f.ComponentId == kept)
                .ExecuteUpdateAsync(f => f.SetProperty(x => x.AlertEmail, true), Cancel);
        }

        await FollowAsync(client, vendor.Id, [kept]);

        await using var check = CreateDb();
        var follow = await check.Follows.AsNoTracking().SingleAsync(f => f.UserId == user, Cancel);
        Assert.Equal(kept, follow.ComponentId);
        Assert.True(follow.AlertEmail);
    }

    [Fact]
    public async Task Sending_the_same_choice_twice_changes_nothing()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);
        var chosen = vendor.Components[0].Id;

        await FollowAsync(client, vendor.Id, [chosen, chosen]);
        await FollowAsync(client, vendor.Id, [chosen]);

        await using var db = CreateDb();
        Assert.Equal(1, await db.Follows.CountAsync(f => f.UserId == user, Cancel));
    }

    [Fact]
    public async Task An_empty_choice_is_refused_rather_than_unfollowing()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);

        using var response = await client.PutAsJsonAsync($"/api/follows/{vendor.Id}", new SetFollowsRequest([]), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_vendors_component_is_refused_and_nothing_is_saved()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        var other = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);

        using var response = await client.PutAsJsonAsync(
            $"/api/follows/{vendor.Id}",
            new SetFollowsRequest([vendor.Components[0].Id, other.Components[0].Id]),
            Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = CreateDb();
        Assert.False(await db.Follows.AnyAsync(f => f.UserId == user, Cancel));
    }

    [Fact]
    public async Task A_vendor_outside_the_catalog_cannot_be_followed_or_read()
    {
        var user = await _seed.AddGuestAsync();
        var custom = await AddListedVendorAsync(Unique());
        await ChangeVendorAsync(custom.Id, v => v.SetProperty(x => x.IsCatalog, false));
        var disabled = await _seed.AddVendorAsync(Unique(), ComponentStatus.Operational);
        using var client = ClientFor(user);

        foreach (var id in new[] { custom.Id, disabled.Id, Guid.NewGuid() })
        {
            using var follow = await client.PutAsJsonAsync($"/api/follows/{id}", new SetFollowsRequest(null), Cancel);
            using var detail = await client.GetAsync($"/api/catalog/{id}", Cancel);
            Assert.Equal(HttpStatusCode.NotFound, follow.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        }
    }

    [Fact]
    public async Task Unfollowing_removes_the_vendor_and_can_be_repeated()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);
        await FollowAsync(client, vendor.Id, null);

        using var first = await client.DeleteAsync($"/api/follows/{vendor.Id}", Cancel);
        using var second = await client.DeleteAsync($"/api/follows/{vendor.Id}", Cancel);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json, Cancel);
        Assert.Empty(dashboard!.Vendors);
    }

    [Fact]
    public async Task A_vendor_that_left_the_catalog_can_still_be_unfollowed()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);
        await FollowAsync(client, vendor.Id, null);
        await ChangeVendorAsync(vendor.Id, v => v.SetProperty(x => x.IsEnabled, false));

        using var response = await client.DeleteAsync($"/api/follows/{vendor.Id}", Cancel);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = CreateDb();
        Assert.False(await db.Follows.AnyAsync(f => f.UserId == user, Cancel));
    }

    [Fact]
    public async Task One_user_never_sees_or_changes_anothers_follows()
    {
        var owner = await _seed.AddGuestAsync();
        var other = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var ownerClient = ClientFor(owner);
        using var otherClient = ClientFor(other);
        await FollowAsync(ownerClient, vendor.Id, null);

        var seenByOther = await otherClient.GetFromJsonAsync<CatalogVendorDetailDto>($"/api/catalog/{vendor.Id}", Json, Cancel);
        var listedForOther = Assert.Single((await GetCatalogAsync(other, $"search={vendor.Name}")).Vendors);
        using var unfollow = await otherClient.DeleteAsync($"/api/follows/{vendor.Id}", Cancel);
        await FollowAsync(otherClient, vendor.Id, [vendor.Components[0].Id]);

        Assert.All(seenByOther!.Components, c => Assert.False(c.Followed));
        Assert.Equal(0, listedForOther.FollowedComponentCount);
        await using var db = CreateDb();
        Assert.Equal(2, await db.Follows.CountAsync(f => f.UserId == owner, Cancel));
    }

    [Fact]
    public async Task A_guest_stops_at_the_guest_limit_but_can_still_change_a_followed_vendor()
    {
        var user = await _seed.AddGuestAsync();
        using var client = ClientFor(user);
        Vendor last = null!;
        for (var i = 0; i < SetFollowsCommand.GuestVendorLimit; i++)
        {
            last = await AddListedVendorAsync(Unique());
            await FollowAsync(client, last.Id, null);
        }

        var extra = await AddListedVendorAsync(Unique());
        using var refused = await client.PutAsJsonAsync($"/api/follows/{extra.Id}", new SetFollowsRequest(null), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var changed = await FollowAsync(client, last.Id, [last.Components[0].Id]);
        Assert.Single(changed.Components, c => c.Followed);
    }

    [Fact]
    public async Task An_account_can_follow_more_than_the_guest_limit()
    {
        var user = await AddAccountAsync();
        using var client = ClientFor(user);

        for (var i = 0; i <= SetFollowsCommand.GuestVendorLimit; i++)
        {
            var vendor = await AddListedVendorAsync(Unique());
            await FollowAsync(client, vendor.Id, null);
        }

        await using var db = CreateDb();
        Assert.Equal(2 * (SetFollowsCommand.GuestVendorLimit + 1), await db.Follows.CountAsync(f => f.UserId == user, Cancel));
    }

    /// <summary>
    /// Timing alone rarely makes requests overlap, so this checks the lock itself: while another
    /// transaction holds the user's row, a follow change has to wait for it. FOR NO KEY UPDATE is on
    /// purpose: it conflicts with the command's FOR UPDATE, but not with the key lock that inserting
    /// a follow takes, so only the command's own lock can make the request wait.
    /// </summary>
    [Fact]
    public async Task A_follow_change_waits_for_the_users_other_changes()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await AddListedVendorAsync(Unique());
        using var client = ClientFor(user);
        await using var holder = CreateDb();
        await using var transaction = await holder.Database.BeginTransactionAsync(Cancel);
        await holder.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {user} FOR NO KEY UPDATE", Cancel);

        var pending = client.PutAsJsonAsync($"/api/follows/{vendor.Id}", new SetFollowsRequest(null), Cancel);
        await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2), Cancel));
        Assert.False(pending.IsCompleted, "The request should wait for the lock on the user's row.");

        await transaction.RollbackAsync(Cancel);
        using var response = await pending;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string Unique() =>$"V{Guid.NewGuid():N}"[..16];

    /// <summary>An enabled catalog vendor with two components. No poller runs in these tests.</summary>
    private async Task<Vendor> AddListedVendorAsync(string name, VendorCategory category = VendorCategory.Cloud)
    {
        var vendor = await _seed.AddVendorAsync(name, ComponentStatus.Operational, ComponentStatus.Operational);
        await ChangeVendorAsync(vendor.Id, v => v.SetProperty(x => x.IsEnabled, true).SetProperty(x => x.Category, category));
        return vendor;
    }

    private async Task ChangeVendorAsync(
        Guid id,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<Vendor>> change)
    {
        await using var db = CreateDb();
        await db.Vendors.Where(v => v.Id == id).ExecuteUpdateAsync(change, Cancel);
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

    private async Task<CatalogDto> GetCatalogAsync(Guid user, string query)
    {
        using var client = ClientFor(user);
        return (await client.GetFromJsonAsync<CatalogDto>($"/api/catalog?{query}", Json, Cancel))!;
    }

    private static async Task<CatalogVendorDetailDto> FollowAsync(HttpClient client, Guid vendorId, Guid[]? components)
    {
        using var response = await client.PutAsJsonAsync($"/api/follows/{vendorId}", new SetFollowsRequest(components), Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CatalogVendorDetailDto>(Json, Cancel))!;
    }

    private HttpClient ClientFor(Guid user)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.Header, user.ToString());
        return client;
    }

    private StatusTideDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<StatusTideDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new StatusTideDbContext(options);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DashboardExtensions.ConfigureJson(json);
        return json;
    }
}
