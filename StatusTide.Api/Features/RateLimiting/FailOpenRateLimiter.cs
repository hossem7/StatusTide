using System.Threading.RateLimiting;

using StackExchange.Redis;

namespace StatusTide.Api.Features.RateLimiting;

/// <summary>
/// Lets requests through when Redis can't be reached, instead of failing every request with a
/// 500. Losing rate limits for a few minutes is better than losing the whole API, and every
/// endpoint that matters still has its own checks (follow limits, the demo cooldown).
/// </summary>
public sealed class FailOpenRateLimiter(RateLimiter inner, ILogger<FailOpenRateLimiter> logger) : RateLimiter
{
    private static readonly TimeSpan LogEvery = TimeSpan.FromSeconds(30);
    private static long s_lastLoggedAt = long.MinValue;

    public override TimeSpan? IdleDuration => inner.IdleDuration;

    // The Redis limiter doesn't keep statistics, and throws if asked.
    public override RateLimiterStatistics? GetStatistics() => null;

    // The Redis limiter can't answer without a round trip, so this always says "not acquired"
    // and the middleware then calls AcquireAsync.
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => inner.AttemptAcquire(permitCount);

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.AcquireAsync(permitCount, cancellationToken);
        }
        catch (Exception e) when (e is RedisException or RedisTimeoutException)
        {
            LogNowAndThen(e);
            return AllowedLease.Instance;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    // While Redis is down every request lands here, so log once per period, not per request.
    private void LogNowAndThen(Exception e)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref s_lastLoggedAt);
        if (now - last >= LogEvery.TotalMilliseconds && Interlocked.CompareExchange(ref s_lastLoggedAt, now, last) == last)
        {
            logger.LogWarning(e, "Rate limits are off because Redis can't be reached");
        }
    }

    private sealed class AllowedLease : RateLimitLease
    {
        public static readonly AllowedLease Instance = new();

        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
