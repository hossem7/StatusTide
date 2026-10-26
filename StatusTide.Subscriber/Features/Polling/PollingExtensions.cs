using Microsoft.Extensions.DependencyInjection.Extensions;

using StatusTide.Subscriber.Features.Polling.Statuspage;

namespace StatusTide.Subscriber.Features.Polling;

public static class PollingExtensions
{
    /// <summary>Used when "Polling:UserAgent" is not set. The domain is a placeholder until it is registered.</summary>
    public const string DefaultUserAgent = "StatusTideBot/1.0 (+https://statustide.example.com/about)";

    /// <summary>
    /// Registers the feed client, one <see cref="IStatusReader"/> per supported format, and what
    /// the poll consumer needs. Every request names our bot, so vendors can see who polls them.
    /// </summary>
    public static IServiceCollection AddPolling(this IServiceCollection services, IConfiguration configuration)
    {
        var userAgent = configuration["Polling:UserAgent"] is { Length: > 0 } configured ? configured : DefaultUserAgent;

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<FeedClient>(http => http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        services.AddTransient<IStatusReader, StatuspageReader>();
        services.AddSingleton<VendorPollState>();
        services.AddScoped<SnapshotWriter>();
        return services;
    }
}
