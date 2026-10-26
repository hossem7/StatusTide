using System.Text.Json;

using StackExchange.Redis;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// What we remember between polls of a vendor, in Redis: the fingerprint of the last
/// snapshot, the feed's validators for conditional requests, and how many polls in a row
/// failed. Losing any of it is safe; the next poll just does a little more work.
/// </summary>
public class VendorPollState(IConnectionMultiplexer redis)
{
    /// <summary>Long enough to outlive any poll interval, short enough that removed vendors clean up.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(1);

    public static string FingerprintKey(Guid vendorId) => $"fp:vendor:{vendorId}";
    public static string ValidatorsKey(Guid vendorId) => $"etag:vendor:{vendorId}";
    public static string FailuresKey(Guid vendorId) => $"backoff:vendor:{vendorId}";

    public async Task<FeedValidators?> GetValidatorsAsync(Guid vendorId)
    {
        var json = await redis.GetDatabase().StringGetAsync(ValidatorsKey(vendorId));
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<FeedValidators>(json.ToString());
    }

    public async Task<string?> GetFingerprintAsync(Guid vendorId) =>
        await redis.GetDatabase().StringGetAsync(FingerprintKey(vendorId));

    /// <summary>
    /// Records a successful read: the new validators, the new fingerprint when there is one
    /// (a 304 keeps the old one), and a reset failure count.
    /// </summary>
    public async Task RecordSuccessAsync(Guid vendorId, string? fingerprint, FeedValidators validators)
    {
        var db = redis.GetDatabase();
        var tasks = new List<Task>
        {
            validators.IsEmpty
                ? db.KeyDeleteAsync(ValidatorsKey(vendorId))
                : db.StringSetAsync(ValidatorsKey(vendorId), JsonSerializer.Serialize(validators), Expiry),
            db.KeyDeleteAsync(FailuresKey(vendorId)),
        };

        if (fingerprint is not null)
        {
            tasks.Add(db.StringSetAsync(FingerprintKey(vendorId), fingerprint, Expiry));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Counts a failed read and returns how many happened in a row. The fingerprint and
    /// validators are dropped, so the first good read after it is a full read that counts
    /// as a change and replaces the "unknown" statuses.
    /// </summary>
    public async Task<long> RecordFailureAsync(Guid vendorId)
    {
        var db = redis.GetDatabase();
        var failures = await db.StringIncrementAsync(FailuresKey(vendorId));
        await Task.WhenAll(
            db.KeyExpireAsync(FailuresKey(vendorId), Expiry),
            db.KeyDeleteAsync([FingerprintKey(vendorId), ValidatorsKey(vendorId)]));
        return failures;
    }
}
