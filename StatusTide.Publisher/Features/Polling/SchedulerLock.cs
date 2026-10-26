using StackExchange.Redis;

namespace StatusTide.Publisher.Features.Polling;

/// <summary>
/// Makes sure only one Publisher queues polls, even when two run for a moment: during a
/// rollout, or when one runs in Compose and another from the IDE. Whoever holds
/// "lock:scheduler" queues; everyone else skips the tick.
/// </summary>
public sealed class SchedulerLock(IConnectionMultiplexer redis) : IAsyncDisposable
{
    public const string Key = "lock:scheduler";

    /// <summary>
    /// How long the lock outlives a holder that stops renewing it, for example after a crash.
    /// Several ticks long, so a slow tick never loses it.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(60);

    private readonly string _token = $"{Environment.MachineName}:{Guid.NewGuid()}";

    /// <summary>Renews the lock if we hold it, or takes it if it is free.</summary>
    public async Task<bool> TryAcquireAsync()
    {
        var db = redis.GetDatabase();
        return await db.LockExtendAsync(Key, _token, Expiry)
            || await db.LockTakeAsync(Key, _token, Expiry);
    }

    /// <summary>Releases the lock on shutdown, so a replacement Publisher can start at once.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await redis.GetDatabase().LockReleaseAsync(Key, _token);
        }
        catch (RedisException)
        {
            // Redis is gone. The lock expires on its own.
        }
    }
}
