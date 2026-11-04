using Microsoft.AspNetCore.HttpOverrides;

namespace StatusTide.Api.Features.RateLimiting;

public class TrustedProxyOptions
{
    /// <summary>
    /// Networks whose X-Forwarded-For header we believe, in CIDR form. Empty means only
    /// loopback, so in Compose and plain `dotnet run` the connection's own address is used.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxies in a row may add to X-Forwarded-For.</summary>
    public int ForwardLimit { get; set; } = 1;
}

/// <summary>
/// Behind Traefik (and later CloudFront) every request arrives from a proxy's address, so rate
/// limits and logs need the client address from X-Forwarded-For. Only trusted proxies may set
/// it: a header from anyone else is ignored, or a client could name any address it likes and
/// get a fresh limit with every request.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders").Get<TrustedProxyOptions>() ?? new TrustedProxyOptions();

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = proxies.ForwardLimit;
            foreach (var network in proxies.KnownNetworks)
            {
                forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
