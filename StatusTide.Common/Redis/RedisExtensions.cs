using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

namespace StatusTide.Common.Redis;

public static class RedisExtensions
{
    /// <summary>
    /// Registers one shared Redis connection, using the "Redis" connection string
    /// (host:port). StackExchange.Redis is built to be shared, so it is a singleton.
    /// </summary>
    public static IServiceCollection AddStatusTideRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Redis' is not configured.");
        }

        // Keep retrying in the background when Redis starts after the app, instead of
        // failing startup.
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        return services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
    }
}
