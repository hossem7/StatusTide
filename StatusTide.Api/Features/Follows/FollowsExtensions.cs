using StatusTide.Api.Features.RateLimiting;

namespace StatusTide.Api.Features.Follows;

public static class FollowsExtensions
{
    public const string RateLimitPolicy = "follows";

    /// <summary>Follow changes one user may make per minute.</summary>
    public const int ChangesPerMinute = 30;

    /// <summary>
    /// Registers follow changes and a per-user limit on them. Each change is a locked database
    /// transaction, and the screen only sends one per click, so 30 a minute is plenty for a person.
    /// </summary>
    public static IServiceCollection AddFollows(this IServiceCollection services)
    {
        services.AddScoped<SetFollowsCommand>();
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, http =>
            RateLimitingExtensions.PerUser(http, RateLimitPolicy, ChangesPerMinute, TimeSpan.FromMinutes(1))));

        return services;
    }
}
