using Microsoft.Extensions.DependencyInjection.Extensions;

using StatusTide.Subscriber.Features.Polling.Statuspage;

namespace StatusTide.Subscriber.Features.Polling;

public static class PollingExtensions
{
    /// <summary>Registers the feed client and one <see cref="IStatusReader"/> per supported format.</summary>
    public static IServiceCollection AddPolling(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<FeedClient>();
        services.AddTransient<IStatusReader, StatuspageReader>();
        return services;
    }
}
