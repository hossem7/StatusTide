using System.Net;
using System.Net.Http.Json;

using MassTransit.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StackExchange.Redis;

using StatusTide.Api.Features.Demo;
using StatusTide.Common.Demo;
using StatusTide.Contracts.Polling;
using StatusTide.Data.Seed;

namespace StatusTide.IntegrationTests.Demo;

/// <summary>
/// "Simulate an outage" through the real API and Redis. The clock is fake, so cooldowns can be
/// skipped without waiting.
/// </summary>
[Collection(DemoOutageCollection.Name)]
public sealed class DemoEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly RedisFixture _redis;
    private readonly ApiFactory _factory;

    public DemoEndpointTests(PostgresFixture postgres, RedisFixture redis)
    {
        _redis = redis;
        _factory = new ApiFactory(postgres, "demo_api", services => services
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(redis.Connection));
    }

    public async ValueTask InitializeAsync() => await _redis.Connection.GetDatabase().KeyDeleteAsync(DemoOutageStore.Key);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Starting_an_outage_queues_a_poll_of_the_demo_vendor()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/api/demo/outage", null, Cancel);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var outage = await response.Content.ReadFromJsonAsync<DemoOutageDto>(Cancel);
        Assert.Equal("active", outage!.State);
        Assert.Equal(Now, outage.StartedAt);
        Assert.Equal(Now + DemoOutageStore.Duration, outage.ResolvesAt);

        var harness = _factory.Services.GetTestHarness();
        Assert.True(await harness.Published.Any<PollVendor>(m => m.Context.Message.VendorId == AcmeCloudSeed.VendorId, Cancel));
    }

    [Fact]
    public async Task A_second_outage_waits_for_the_first_to_resolve_and_cool_down()
    {
        using var client = _factory.CreateClient();
        (await client.PostAsync("/api/demo/outage", null, Cancel)).Dispose();

        using var running = await client.PostAsync("/api/demo/outage", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
        var wait = DemoOutageStore.Duration + DemoOutageStore.Cooldown;
        Assert.Equal(wait, running.Headers.RetryAfter!.Delta);

        _time.Advance(DemoOutageStore.Duration);
        using var coolingDown = await client.PostAsync("/api/demo/outage", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, coolingDown.StatusCode);
        Assert.Equal("coolingDown", (await client.GetFromJsonAsync<DemoOutageDto>("/api/demo/outage", Cancel))!.State);

        _time.Advance(DemoOutageStore.Cooldown);
        using var again = await client.PostAsync("/api/demo/outage", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Fact]
    public async Task Resolving_ends_the_outage_now_and_queues_a_poll()
    {
        using var client = _factory.CreateClient();
        (await client.PostAsync("/api/demo/outage", null, Cancel)).Dispose();
        _time.Advance(TimeSpan.FromSeconds(40));

        using var response = await client.DeleteAsync("/api/demo/outage", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var outage = await response.Content.ReadFromJsonAsync<DemoOutageDto>(Cancel);
        Assert.Equal("coolingDown", outage!.State);
        Assert.Equal(Now.AddSeconds(40), outage.ResolvesAt);
        Assert.Equal(Now.AddSeconds(40) + DemoOutageStore.Cooldown, outage.NextStartAt);

        var harness = _factory.Services.GetTestHarness();
        Assert.Equal(2, harness.Published.Select<PollVendor>(Cancel).Count());
    }

    [Fact]
    public async Task Resolving_without_an_outage_is_not_found()
    {
        using var client = _factory.CreateClient();

        using var response = await client.DeleteAsync("/api/demo/outage", Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("none", (await client.GetFromJsonAsync<DemoOutageDto>("/api/demo/outage", Cancel))!.State);
    }

    [Fact]
    public async Task Too_many_requests_from_one_address_are_limited()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < DemoExtensions.PermitsPerMinute; i++)
        {
            using var allowed = await client.DeleteAsync("/api/demo/outage", Cancel);
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }

        using var limited = await client.PostAsync("/api/demo/outage", null, Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.False(await _redis.Connection.GetDatabase().KeyExistsAsync(DemoOutageStore.Key));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
}

/// <summary>
/// Every test that touches the shared demo outage key in Redis runs in this collection, one at
/// a time, so they cannot start or resolve each other's outage.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DemoOutageCollection
{
    public const string Name = "Demo outage";
}
