using System.Net;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using StackExchange.Redis;

using StatusTide.Api.Features.RateLimiting;

namespace StatusTide.UnitTests.RateLimiting;

public class ClientAddressTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void Addresses_are_grouped_so_one_network_shares_a_limit(string address, string expected)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.Equal(expected, RateLimitingExtensions.ClientAddress(http));
    }

    [Fact]
    public void A_request_with_no_address_still_gets_a_partition() =>
        Assert.Equal("unknown", RateLimitingExtensions.ClientAddress(new DefaultHttpContext()));
}

public class FailOpenRateLimiterTests
{
    [Fact]
    public async Task A_redis_failure_lets_the_request_through()
    {
        using var limiter = new FailOpenRateLimiter(new BrokenLimiter(), NullLogger<FailOpenRateLimiter>.Instance);

        using var lease = await limiter.AcquireAsync(1, TestContext.Current.CancellationToken);

        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task A_refusal_from_redis_is_kept()
    {
        using var inner = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) });
        using var limiter = new FailOpenRateLimiter(inner, NullLogger<FailOpenRateLimiter>.Instance);

        using var first = await limiter.AcquireAsync(1, TestContext.Current.CancellationToken);
        using var second = await limiter.AcquireAsync(1, TestContext.Current.CancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    private sealed class BrokenLimiter : RateLimiter
    {
        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
            throw new RedisException("No connection");

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
            throw new RedisException("No connection");
    }
}
