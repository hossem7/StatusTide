using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using StatusTide.Common.Demo;
using StatusTide.Common.Outbound;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.Subscriber.Features.Checks;

public static class CheckExtensions
{
    /// <summary>
    /// Registers the health probe. It sends the same User-Agent as polling, so vendors can see
    /// who calls them, never follows redirects and goes through the SSRF guard.
    /// </summary>
    public static IServiceCollection AddChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var userAgent = configuration["Polling:UserAgent"] is { Length: > 0 } configured ? configured : PollingExtensions.DefaultUserAgent;

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DemoOutageStore>();
        services.Configure<OutboundOptions>(configuration.GetSection("Outbound"));
        services.AddHttpClient<HealthProbe>(http => http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent))
            .ConfigurePrimaryHttpMessageHandler(provider =>
                OutboundHandler.Create(provider.GetRequiredService<IOptions<OutboundOptions>>().Value, followRedirects: false));
        return services;
    }
}
