using System.Threading.RateLimiting;

using Microsoft.Extensions.DependencyInjection.Extensions;

using StatusTide.Common.Demo;

namespace StatusTide.Api.Features.Demo;

public static class DemoExtensions
{
    public const string RateLimitPolicy = "demo";

    /// <summary>Requests per minute one address may send to start or resolve the demo outage.</summary>
    public const int PermitsPerMinute = 10;

    /// <summary>
    /// Registers the demo outage store and a per-address limit on its write endpoints. The shared
    /// cooldown already stops repeat outages; this stops one client from flooding the endpoint.
    /// The full rate limits come with issue #12.
    /// </summary>
    public static IServiceCollection AddDemo(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DemoOutageStore>();
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }
}
