using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using StackExchange.Redis;

using StatusTide.Api.Features.RateLimiting;

namespace StatusTide.Api.Features.Accounts;

/// <summary>
/// Limits password attempts per email, on top of the per-address limit. Without it, someone
/// with many addresses could keep guessing one account's password.
/// </summary>
/// <remarks>
/// Every attempt is counted before the password is checked, in one Redis call, so a burst of
/// parallel attempts can't all slip in before the count catches up. A correct password clears
/// the count. Unknown emails are counted the same way, so the answer never shows which
/// addresses have accounts. Keys hold a hash of the email, not the email itself.
/// </remarks>
public class SignInLockout(IConnectionMultiplexer redis, IOptions<RateLimitOptions> options, ILogger<SignInLockout> logger)
{
    /// <summary>Attempts allowed per email before it is locked.</summary>
    public const int AttemptsPerPeriod = 5;

    /// <summary>The count starts at the first attempt and is gone this long after it.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

    // Returns -1 when the attempt may go ahead, or the milliseconds left on the lock.
    private const string CountScript = """
        local attempts = redis.call('INCR', KEYS[1])
        if attempts == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        if attempts > tonumber(ARGV[1]) then
            return redis.call('PTTL', KEYS[1])
        end
        return -1
        """;

    /// <summary>Counts an attempt. Returns how long the email is locked for, or null to go ahead.</summary>
    public async Task<TimeSpan?> CountAttemptAsync(string email)
    {
        try
        {
            var left = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                CountScript,
                [Key(email)],
                [AttemptsPerPeriod, (long)Period.TotalMilliseconds]);
            return left < 0 ? null : TimeSpan.FromMilliseconds(left);
        }
        catch (Exception e) when (e is RedisException or RedisTimeoutException)
        {
            // Same choice as the rate limits: the per-address limit still applies.
            logger.LogWarning(e, "Sign-in lockout skipped because Redis can't be reached");
            return null;
        }
    }

    /// <summary>Clears the count after a correct password.</summary>
    public async Task ClearAsync(string email)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(email));
        }
        catch (Exception e) when (e is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(e, "Sign-in lockout count not cleared because Redis can't be reached");
        }
    }

    private RedisKey Key(string email) =>
        $"{options.Value.KeyPrefix}signin:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email)))}";
}
