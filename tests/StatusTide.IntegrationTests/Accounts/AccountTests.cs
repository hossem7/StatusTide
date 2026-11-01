using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using MassTransit.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using StatusTide.Api.Features.Accounts;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Guests;
using StatusTide.Api.Features.Users;
using StatusTide.Contracts.Accounts;
using StatusTide.Data;
using StatusTide.Data.Seed;
using StatusTide.Data.Users;

namespace StatusTide.IntegrationTests.Accounts;

/// <summary>Accounts through the real API and PostgreSQL, with a real cookie jar.</summary>
public sealed class AccountTests : IAsyncDisposable
{
    private const string Password = "correct-horse-battery";
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly PostgresFixture _postgres;
    private readonly RedisFixture _redis;
    private readonly ApiFactory _factory;

    public AccountTests(PostgresFixture postgres, RedisFixture redis)
    {
        _postgres = postgres;
        _redis = redis;
        _factory = new ApiFactory(postgres, "accounts_api", services => services.AddSingleton(redis.Connection));
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"{Guid.NewGuid():N}@acme.example";

    [Fact]
    public async Task Signing_up_returns_a_working_token_and_a_locked_down_refresh_cookie()
    {
        using var client = CreateBrowser();
        var email = NewEmail();

        using var response = await PostAsync(client, "register", new RegisterRequest(email, Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{RefreshCookie.Name}=", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/auth", cookie);

        var auth = await ReadAuthAsync(response);
        Assert.Equal(email, auth.User.Email);
        Assert.False(auth.User.IsGuest);
        Assert.False(auth.User.EmailConfirmed);

        using var fresh = CreateBrowser();
        var me = await MeAsync(fresh, auth.AccessToken);
        Assert.Equal(auth.User.Id, me.Id);
    }

    [Fact]
    public async Task The_password_is_stored_only_as_a_hash()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());

        await using var db = CreateDb();
        var user = await db.Users.SingleAsync(u => u.Id == auth.User.Id, Cancel);

        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash);
    }

    [Fact]
    public async Task Signing_up_queues_the_confirmation_email()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        Assert.True(await harness.Published.Any<SendEmailConfirmation>(m => m.Context.Message.UserId == auth.User.Id, Cancel));
    }

    [Fact]
    public async Task A_guest_who_signs_up_keeps_their_row_and_follows()
    {
        using var client = CreateBrowser();
        using var started = await client.PostAsync("/api/guest", null, Cancel);
        var guest = (await started.Content.ReadFromJsonAsync<MeDto>(Json, Cancel))!;

        using var response = await PostAsync(client, "register", new RegisterRequest(NewEmail(), Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await ReadAuthAsync(response);
        Assert.Equal(guest.Id, auth.User.Id);
        Assert.False(auth.User.IsGuest);
        Assert.Null(auth.User.GuestExpiresAt);

        var dashboard = await GetDashboardAsync(client, auth.AccessToken);
        Assert.Equal(AcmeCloudSeed.VendorId, Assert.Single(dashboard.Vendors).Id);

        await using var db = CreateDb();
        var user = await db.Users.SingleAsync(u => u.Id == guest.Id, Cancel);
        Assert.False(user.IsGuest);
        Assert.Null(user.GuestTokenHash);
    }

    [Fact]
    public async Task Signing_up_ends_the_guest_cookie_on_both_sides()
    {
        using var noJar = CreateClient(handleCookies: false);
        using var started = await noJar.PostAsync("/api/guest", null, Cancel);
        var guestCookie = Assert.Single(started.Headers.GetValues("Set-Cookie")).Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(NewEmail(), Password)),
        };
        request.Headers.Add("Cookie", guestCookie);
        using var response = await noJar.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var deletion = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith($"{GuestCookie.Name}="));
        Assert.Contains("secure", deletion);
        Assert.Contains("path=/", deletion);
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion);

        using var old = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        old.Headers.Add("Cookie", guestCookie);
        using var afterwards = await noJar.SendAsync(old, Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task A_taken_email_is_refused_whatever_its_case_and_a_guest_stays_a_guest()
    {
        var email = NewEmail();
        using (var first = CreateBrowser())
        {
            await RegisterAsync(first, email);
        }

        using var client = CreateBrowser();
        using var started = await client.PostAsync("/api/guest", null, Cancel);
        var guest = (await started.Content.ReadFromJsonAsync<MeDto>(Json, Cancel))!;

        using var response = await PostAsync(client, "register", new RegisterRequest(email.ToUpperInvariant(), Password));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var me = await client.GetFromJsonAsync<MeDto>("/api/me", Json, Cancel);
        Assert.Equal(guest.Id, me!.Id);
        Assert.True(me.IsGuest);
    }

    [Fact]
    public async Task A_signed_in_account_cannot_sign_up_again()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        using var response = await PostAsync(client, "register", new RegisterRequest(NewEmail(), Password));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("person@acme.example", "too-short")]
    [InlineData("not-an-email", "correct-horse-battery")]
    public async Task A_bad_email_or_password_is_refused(string email, string password)
    {
        using var client = CreateBrowser();

        using var response = await PostAsync(client, "register", new RegisterRequest(email, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_works_with_the_right_password_in_any_email_case()
    {
        var email = NewEmail();
        Guid id;
        using (var first = CreateBrowser())
        {
            id = (await RegisterAsync(first, email)).User.Id;
        }

        using var client = CreateBrowser();
        using var response = await PostAsync(client, "login", new LoginRequest(email.ToUpperInvariant(), Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, (await ReadAuthAsync(response)).User.Id);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_look_the_same()
    {
        var email = NewEmail();
        using var client = CreateBrowser();
        await RegisterAsync(client, email);

        using var wrong = await PostAsync(client, "login", new LoginRequest(email, "wrong-password-here"));
        using var unknown = await PostAsync(client, "login", new LoginRequest(NewEmail(), Password));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await DetailAsync(wrong), await DetailAsync(unknown));
    }

    [Fact]
    public async Task Refreshing_replaces_the_cookie_and_returns_a_working_token()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());

        using var response = await PostAsync(client, "refresh");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith($"{RefreshCookie.Name}=", Assert.Single(response.Headers.GetValues("Set-Cookie")));
        var refreshed = await ReadAuthAsync(response);
        using var fresh = CreateBrowser();
        Assert.Equal(auth.User.Id, (await MeAsync(fresh, refreshed.AccessToken)).Id);
    }

    [Fact]
    public async Task Without_a_refresh_cookie_refresh_is_unauthorized()
    {
        using var client = CreateBrowser();

        using var response = await PostAsync(client, "refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_just_used_refresh_token_still_works_without_replacing_the_cookie()
    {
        using var noJar = CreateClient(handleCookies: false);
        var oldCookie = await RegisterForCookieAsync(noJar);
        using var first = await RefreshWithAsync(noJar, oldCookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // A second tab that sent the same cookie at the same moment.
        using var second = await RefreshWithAsync(noJar, oldCookie);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(second.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_used_refresh_token_coming_back_later_revokes_the_whole_sign_in()
    {
        using var noJar = CreateClient(handleCookies: false);
        var oldCookie = await RegisterForCookieAsync(noJar);
        using var first = await RefreshWithAsync(noJar, oldCookie);
        var newCookie = Assert.Single(first.Headers.GetValues("Set-Cookie")).Split(';')[0];

        var replacedAt = DateTimeOffset.UtcNow - AccountPolicy.RefreshReuseGrace - TimeSpan.FromSeconds(1);
        await using (var db = CreateDb())
        {
            var hash = SecureToken.Hash(oldCookie.Split('=', 2)[1]);
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.ReplacedAt, replacedAt), Cancel);
        }

        using var stolen = await RefreshWithAsync(noJar, oldCookie);
        using var victim = await RefreshWithAsync(noJar, newCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, stolen.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, victim.StatusCode);
    }

    [Fact]
    public async Task An_expired_refresh_token_does_not_work()
    {
        using var noJar = CreateClient(handleCookies: false);
        var cookie = await RegisterForCookieAsync(noJar);
        await using (var db = CreateDb())
        {
            var hash = SecureToken.Hash(cookie.Split('=', 2)[1]);
            var past = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, past), Cancel);
        }

        using var response = await RefreshWithAsync(noJar, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_refresh_token_and_clears_the_cookie()
    {
        using var noJar = CreateClient(handleCookies: false);
        var cookie = await RegisterForCookieAsync(noJar);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", cookie);
        using var logout = await noJar.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", Assert.Single(logout.Headers.GetValues("Set-Cookie")));
        using var refresh = await RefreshWithAsync(noJar, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, auth.User.Id.ToString())]),
            Issuer = "statustide",
            Audience = "statustide",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-different-key-that-is-long-enough-to-sign")),
                SecurityAlgorithms.HmacSha256),
        });

        await AssertRefusedAsync(forged);
    }

    [Fact]
    public async Task An_unsigned_token_is_refused()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var body = Base64UrlEncoder.Encode(
            $$"""{"sub":"{{auth.User.Id}}","iss":"statustide","aud":"statustide","exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}""");

        await AssertRefusedAsync($"{header}.{body}.");
    }

    [Fact]
    public async Task A_bad_token_never_falls_back_to_the_guest_cookie()
    {
        using var client = CreateBrowser();
        using var started = await client.PostAsync("/api/guest", null, Cancel);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        using var response = await client.GetAsync("/api/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_query_string_token_only_counts_on_the_hub()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());

        using var fresh = CreateBrowser();
        using var response = await fresh.GetAsync($"/api/me?access_token={auth.AccessToken}", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task One_account_never_sees_another_accounts_dashboard()
    {
        // The guest route gives the first account a follow. The second account has none.
        using var owner = CreateBrowser();
        using var started = await owner.PostAsync("/api/guest", null, Cancel);
        var ownerAuth = await RegisterAsync(owner, NewEmail());
        using var other = CreateBrowser();
        var otherAuth = await RegisterAsync(other, NewEmail());

        var ownerDashboard = await GetDashboardAsync(owner, ownerAuth.AccessToken);
        var otherDashboard = await GetDashboardAsync(other, otherAuth.AccessToken);

        Assert.Single(ownerDashboard.Vendors);
        Assert.Empty(otherDashboard.Vendors);
    }

    [Fact]
    public async Task A_confirmation_link_works_once()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());
        var token = await GiveConfirmationTokenAsync(auth.User.Id, DateTimeOffset.UtcNow);

        using var first = await PostAsync(client, "confirm-email", new ConfirmEmailRequest(token));
        using var second = await PostAsync(client, "confirm-email", new ConfirmEmailRequest(token));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.True((await MeAsync(client, auth.AccessToken)).EmailConfirmed);
    }

    [Fact]
    public async Task An_old_confirmation_link_does_not_work()
    {
        using var client = CreateBrowser();
        var auth = await RegisterAsync(client, NewEmail());
        var sentAt = DateTimeOffset.UtcNow - AccountPolicy.ConfirmationLifetime - TimeSpan.FromMinutes(1);
        var token = await GiveConfirmationTokenAsync(auth.User.Id, sentAt);

        using var response = await PostAsync(client, "confirm-email", new ConfirmEmailRequest(token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await MeAsync(client, auth.AccessToken)).EmailConfirmed);
    }

    [Fact]
    public async Task Only_a_signed_in_account_can_ask_for_another_email()
    {
        using var client = CreateBrowser();
        using var nobody = await PostAsync(client, "resend-confirmation");
        Assert.Equal(HttpStatusCode.Unauthorized, nobody.StatusCode);

        using var started = await client.PostAsync("/api/guest", null, Cancel);
        using var guest = await PostAsync(client, "resend-confirmation");
        Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);

        var auth = await RegisterAsync(client, NewEmail());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var account = await PostAsync(client, "resend-confirmation");
        Assert.Equal(HttpStatusCode.Accepted, account.StatusCode);
    }

    [Fact]
    public async Task One_address_can_make_only_a_few_password_attempts_a_minute()
    {
        // A factory of its own, so this test's limit isn't shared with the other tests.
        await using var factory = new ApiFactory(_postgres, "accounts_limit", services => services.AddSingleton(_redis.Connection));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var attempt = new LoginRequest(NewEmail(), Password);

        for (var i = 0; i < AccountsExtensions.AttemptsPerMinute; i++)
        {
            using var allowed = await PostAsync(client, "login", attempt);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var limited = await PostAsync(client, "login", attempt);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private async Task AssertRefusedAsync(string token)
    {
        using var client = CreateBrowser();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> RegisterForCookieAsync(HttpClient noJar)
    {
        using var response = await PostAsync(noJar, "register", new RegisterRequest(NewEmail(), Password));
        response.EnsureSuccessStatusCode();
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
    }

    private static Task<HttpResponseMessage> RefreshWithAsync(HttpClient noJar, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return noJar.SendAsync(request, Cancel);
    }

    private async Task<string> GiveConfirmationTokenAsync(Guid userId, DateTimeOffset sentAt)
    {
        var (token, hash) = SecureToken.Create();
        await using var db = CreateDb();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(
            u => u.SetProperty(x => x.EmailConfirmationHash, hash).SetProperty(x => x.EmailConfirmationSentAt, sentAt),
            Cancel);
        return token;
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string email)
    {
        using var response = await PostAsync(client, "register", new RegisterRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return await ReadAuthAsync(response);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string action, object? body = null) =>
        client.PostAsync($"/api/auth/{action}", body is null ? null : JsonContent.Create(body), Cancel);

    private static async Task<AuthResponse> ReadAuthAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<AuthResponse>(Json, Cancel))!;

    private static async Task<string> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("detail").GetString()!;

    private static async Task<MeDto> MeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeDto>(Json, Cancel))!;
    }

    private static async Task<DashboardDto> GetDashboardAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DashboardDto>(Json, Cancel))!;
    }

    private HttpClient CreateBrowser() => CreateClient(handleCookies: true);

    // HTTPS, since the cookies are Secure and a cookie jar won't send them over plain HTTP.
    private HttpClient CreateClient(bool handleCookies) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = handleCookies });

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
