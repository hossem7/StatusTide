using Microsoft.Extensions.DependencyInjection.Extensions;

using StatusTide.Common.Demo;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.Subscriber.Features.Checks;

public static class CheckExtensions
{
    /// <summary>
    /// Registers the health probe. It sends the same User-Agent as polling, so vendors can see
    /// who calls them, and never follows redirects.
    /// </summary>
    public static IServiceCollection AddChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var userAgent = configuration["Polling:UserAgent"] is { Length: > 0 } configured ? configured : PollingExtensions.DefaultUserAgent;

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DemoOutageStore>();
        services.AddHttpClient<HealthProbe>(http => http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }
}
