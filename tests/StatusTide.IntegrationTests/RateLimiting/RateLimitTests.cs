using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

using StatusTide.Api.Features.Accounts;
using StatusTide.Api.Features.RateLimiting;

namespace StatusTide.IntegrationTests.RateLimiting;

/// <summary>The general limits, kept in the real Redis container.</summary>
public sealed class RateLimitTests(PostgresFixture postgres, RedisFixture redis)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_address_with_no_session_is_limited_with_a_retry_time()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        await UseUpAsync(client, RateLimitingExtensions.AddressRequestsPerMinute);

        using var limited = await client.GetAsync("/api/me", Cancel);
        using var health = await client.GetAsync("/health", Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.InRange(int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After"))), 1, 61);
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_has_a_budget_of_their_own()
    {
        await using var factory = Factory(services => services.AddTestAuth());
        using var anonymous = factory.CreateClient();
        await UseUpAsync(anonymous, RateLimitingExtensions.AddressRequestsPerMinute);
        using var user = factory.CreateClient();
        user.DefaultRequestHeaders.Add(TestAuth.Header, Guid.NewGuid().ToString());

        await UseUpAsync(user, RateLimitingExtensions.UserRequestsPerMinute);
        using var limited = await user.GetAsync("/api/me", Cancel);
        using var otherUser = await SendAsUserAsync(factory, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherUser.StatusCode);
    }

    [Fact]
    public async Task Limits_are_shared_between_api_instances()
    {
        var shared = new Dictionary<string, string?> { ["RateLimits:KeyPrefix"] = $"test-{Guid.NewGuid():N}:" };
        await using var first = Factory(settings: shared);
        await using var second = Factory(settings: shared);
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        var half = RateLimitingExtensions.AddressRequestsPerMinute / 2;

        await UseUpAsync(firstClient, half);
        await UseUpAsync(secondClient, RateLimitingExtensions.AddressRequestsPerMinute - half);
        using var limited = await firstClient.GetAsync("/api/me", Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Requests_still_work_when_redis_is_down()
    {
        // Nothing listens on port 1, so every Redis call fails.
        await using var down = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=200,asyncTimeout=500,syncTimeout=500");
        await using var factory = new ApiFactory(postgres, "rate_limits", services => services.AddSingleton<IConnectionMultiplexer>(down));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var me = await client.GetAsync("/api/me", Cancel);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@acme.example", "not-the-password"), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task A_trusted_proxy_passes_on_the_client_address()
    {
        await using var factory = Factory(
            FromProxy,
            new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "10.42.0.0/16" });

        await UseUpLoginsAsync(factory, "203.0.113.7");
        using var sameClient = await LoginAsync(factory, "203.0.113.7");
        using var otherClient = await LoginAsync(factory, "203.0.113.8");

        Assert.Equal(HttpStatusCode.TooManyRequests, sameClient.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
    }

    [Fact]
    public async Task A_forwarded_address_from_an_unknown_proxy_is_ignored()
    {
        await using var factory = Factory(FromProxy);

        await UseUpLoginsAsync(factory, "203.0.113.7");
        using var spoofed = await LoginAsync(factory, "203.0.113.8");

        Assert.Equal(HttpStatusCode.TooManyRequests, spoofed.StatusCode);
    }

    private ApiFactory Factory(Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null) =>
        new(postgres, "rate_limits", services =>
        {
            services.AddSingleton(redis.Connection);
            configure?.Invoke(services);
        }, settings);

    private static async Task UseUpAsync(HttpClient client, int requests)
    {
        for (var i = 0; i < requests; i++)
        {
            using var response = await client.GetAsync("/api/me", Cancel);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> SendAsUserAsync(ApiFactory factory, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add(TestAuth.Header, userId.ToString());
        return await factory.CreateClient().SendAsync(request, Cancel);
    }

    private static async Task UseUpLoginsAsync(ApiFactory factory, string clientAddress)
    {
        for (var i = 0; i < AccountsExtensions.AttemptsPerMinute; i++)
        {
            using var response = await LoginAsync(factory, clientAddress);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // A new email each time, so only the per-address limit applies.
    private static async Task<HttpResponseMessage> LoginAsync(ApiFactory factory, string clientAddress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest($"{Guid.NewGuid():N}@acme.example", "not-the-password")),
        };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        return await factory.CreateClient().SendAsync(request, Cancel);
    }

    /// <summary>The test server has no connection address, so make every request come from a Traefik pod.</summary>
    private static void FromProxy(IServiceCollection services) => services.AddSingleton<IStartupFilter, ProxyAddressFilter>();

    private sealed class ProxyAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("10.42.0.5");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
