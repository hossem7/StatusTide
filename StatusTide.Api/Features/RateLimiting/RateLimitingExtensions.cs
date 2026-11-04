using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using RedisRateLimiting;

using StackExchange.Redis;

using StatusTide.Api.Features.Users;

namespace StatusTide.Api.Features.RateLimiting;

public class RateLimitOptions
{
    /// <summary>
    /// Put in front of every rate limit key in Redis. Empty in production. Tests give each API
    /// instance its own prefix, so tests that share one Redis don't share limits.
    /// </summary>
    public string KeyPrefix { get; set; } = "";
}

/// <summary>
/// Rate limits kept in Redis, so they hold across API replicas and restarts. Every request
/// passes a general limit per user (or per address when signed out), and features add tighter
/// limits on their own endpoints with <see cref="FixedWindow"/>.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>Requests per minute for one signed-in user or guest, across the whole API.</summary>
    public const int UserRequestsPerMinute = 300;

    /// <summary>Requests per minute for one address with no session, across the whole API.</summary>
    public const int AddressRequestsPerMinute = 120;

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    /// <summary>Call this before the features add their own policies.</summary>
    public static IServiceCollection AddStatusTideRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimits"));
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = RejectAsync;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                // Kubernetes probes the health endpoint from one address, and must never be refused.
                http.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : FixedWindow(
                        http,
                        "all",
                        http.User.UserId() is { } userId ? $"user:{userId}" : $"ip:{ClientAddress(http)}",
                        http.User.UserId() is null ? AddressRequestsPerMinute : UserRequestsPerMinute,
                        OneMinute));
        });

        return services;
    }

    /// <summary>
    /// A fixed window limit in Redis: <paramref name="permitLimit"/> requests per
    /// <paramref name="window"/> for each <paramref name="partition"/> of <paramref name="policy"/>.
    /// </summary>
    public static RateLimitPartition<string> FixedWindow(HttpContext http, string policy, string partition, int permitLimit, TimeSpan window)
    {
        var services = http.RequestServices;
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var logger = services.GetRequiredService<ILogger<FailOpenRateLimiter>>();
        var key = $"{services.GetRequiredService<IOptions<RateLimitOptions>>().Value.KeyPrefix}{policy}:{partition}";

        return RateLimitPartition.Get(key, partitionKey => new FailOpenRateLimiter(
            new RedisFixedWindowRateLimiter<string>(partitionKey, new RedisFixedWindowRateLimiterOptions
            {
                ConnectionMultiplexerFactory = () => redis,
                PermitLimit = permitLimit,
                Window = window,
            }),
            logger));
    }

    /// <summary>Limits each signed-in user, or each address for callers with no session.</summary>
    public static RateLimitPartition<string> PerUser(HttpContext http, string policy, int permitLimit, TimeSpan window) =>
        FixedWindow(
            http,
            policy,
            http.User.UserId() is { } userId ? $"user:{userId}" : $"ip:{ClientAddress(http)}",
            permitLimit,
            window);

    /// <summary>Limits each address, for endpoints people use before they have a session.</summary>
    public static RateLimitPartition<string> PerAddress(HttpContext http, string policy, int permitLimit, TimeSpan window) =>
        FixedWindow(http, policy, $"ip:{ClientAddress(http)}", permitLimit, window);

    /// <summary>
    /// The caller's address, after forwarded headers from trusted proxies are applied. An IPv6
    /// address is cut to its /64 network, since one home or server usually gets a whole /64 and
    /// could otherwise pick a new address for every request.
    /// </summary>
    public static string ClientAddress(HttpContext http)
    {
        if (http.Connection.RemoteIpAddress is not { } address)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        // The Redis limiter rounds the wait down to whole seconds, so add one to round it up.
        var seconds = context.Lease.TryGetMetadata(RateLimitMetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, retryAfter + 1)
            : 60;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();

        await Results.Problem(
                $"Too many requests. Try again in {seconds} seconds.",
                statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(context.HttpContext);
    }
}
