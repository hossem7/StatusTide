using System.Text.Json;

using StackExchange.Redis;

namespace StatusTide.Common.Demo;

/// <summary>A simulated outage of the demo vendor, Acme Cloud.</summary>
public record DemoOutage(DateTimeOffset StartedAt, DateTimeOffset ResolvesAt)
{
    /// <summary>When the next outage may start.</summary>
    public DateTimeOffset CooldownEndsAt => ResolvesAt + DemoOutageStore.Cooldown;

    public bool IsActiveAt(DateTimeOffset now) => now < ResolvesAt;
}

/// <summary>
/// The demo vendor's one shared outage, kept in Redis. The API starts and resolves it and the
/// demo reader turns it into a feed. Every visitor shares it, so an outage resolves itself
/// after <see cref="Duration"/> and the next one waits for <see cref="Cooldown"/>.
/// </summary>
public class DemoOutageStore(IConnectionMultiplexer redis, TimeProvider timeProvider)
{
    public const string Key = "demo:acme-cloud";

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    /// <summary>The latest outage, until its cooldown ends. It may already be resolved.</summary>
    public async Task<DemoOutage?> GetAsync() => (await ReadAsync()).Outage;

    /// <summary>
    /// Starts an outage, or returns the current one and false when an outage is running or
    /// cooling down.
    /// </summary>
    public async Task<(DemoOutage? Outage, bool Started)> TryStartAsync()
    {
        var now = Now();
        var (stored, current) = await ReadAsync();
        if (current is not null && now < current.CooldownEndsAt)
        {
            return (current, false);
        }

        var outage = new DemoOutage(now, now + Duration);
        return await ReplaceAsync(stored, outage) ? (outage, true) : (await GetAsync(), false);
    }

    /// <summary>Resolves the running outage now. Returns null when none is running.</summary>
    public async Task<DemoOutage?> ResolveAsync()
    {
        var now = Now();
        var (stored, current) = await ReadAsync();
        if (current is null || !current.IsActiveAt(now))
        {
            return null;
        }

        var resolved = current with { ResolvesAt = now };
        return await ReplaceAsync(stored, resolved) ? resolved : null;
    }

    // Whole seconds: PostgreSQL keeps microseconds, so a finer time would not match itself
    // after a round trip and the same incident update would be saved again on every poll.
    private DateTimeOffset Now()
    {
        var now = timeProvider.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private async Task<(RedisValue Stored, DemoOutage? Outage)> ReadAsync()
    {
        var stored = await redis.GetDatabase().StringGetAsync(Key);
        return (stored, stored.IsNullOrEmpty ? null : JsonSerializer.Deserialize<DemoOutage>(stored.ToString()));
    }

    /// <summary>
    /// Writes the outage only if Redis still holds <paramref name="expected"/>, so two visitors
    /// clicking at once cannot both start one. The key expires when the cooldown ends.
    /// </summary>
    private async Task<bool> ReplaceAsync(RedisValue expected, DemoOutage outage)
    {
        var db = redis.GetDatabase();
        var transaction = db.CreateTransaction();
        transaction.AddCondition(expected.IsNullOrEmpty
            ? Condition.KeyNotExists(Key)
            : Condition.StringEqual(Key, expected));

        var expiry = outage.CooldownEndsAt - timeProvider.GetUtcNow();
        _ = transaction.StringSetAsync(Key, JsonSerializer.Serialize(outage), expiry);
        return await transaction.ExecuteAsync();
    }
}
