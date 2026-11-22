using Microsoft.Extensions.DependencyInjection.Extensions;

using StatusTide.Api.Features.RateLimiting;

namespace StatusTide.Api.Features.CustomPages;

public static class CustomPagesExtensions
{
    public const string RateLimitPolicy = "custom-pages";

    /// <summary>Previews and adds one user may send per hour, together.</summary>
    public const int RequestsPerHour = 20;

    /// <summary>
    /// Registers custom pages and a per-user limit on previewing and adding them. Each request
    /// makes the Subscriber fetch up to two URLs someone else chose, so the limit is far below
    /// the follow limit: enough to add all 10 pages with a few mistakes on the way.
    /// </summary>
    public static IServiceCollection AddCustomPages(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<FeedDetection>();
        services.AddScoped<AddCustomPageCommand>();
        services.AddScoped<CustomPagesQuery>();
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, http =>
            RateLimitingExtensions.PerUser(http, RateLimitPolicy, RequestsPerHour, TimeSpan.FromHours(1))));

        return services;
    }
}
