extern alias Publisher;

using Publisher::StatusTide.Publisher.Features.Polling;

namespace StatusTide.IntegrationTests.Polling;

public sealed class SchedulerLockTests(RedisFixture redis) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await redis.Connection.GetDatabase().KeyDeleteAsync(SchedulerLock.Key);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Only_one_scheduler_holds_the_lock()
    {
        await using var first = new SchedulerLock(redis.Connection);
        await using var second = new SchedulerLock(redis.Connection);

        Assert.True(await first.TryAcquireAsync());
        Assert.False(await second.TryAcquireAsync());

        // The holder keeps it on later ticks.
        Assert.True(await first.TryAcquireAsync());
        Assert.False(await second.TryAcquireAsync());
    }

    [Fact]
    public async Task Lock_is_released_on_shutdown()
    {
        await using var replacement = new SchedulerLock(redis.Connection);

        await using (var stopping = new SchedulerLock(redis.Connection))
        {
            Assert.True(await stopping.TryAcquireAsync());
        }

        Assert.True(await replacement.TryAcquireAsync());
    }

    [Fact]
    public async Task Lock_expires_when_its_holder_stops_renewing()
    {
        await using var crashed = new SchedulerLock(redis.Connection);
        Assert.True(await crashed.TryAcquireAsync());

        var ttl = await redis.Connection.GetDatabase().KeyTimeToLiveAsync(SchedulerLock.Key);

        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(1), SchedulerLock.Expiry);
    }
}
