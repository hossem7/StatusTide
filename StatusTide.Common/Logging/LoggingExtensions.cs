using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace StatusTide.Common.Logging;

public static class LoggingExtensions
{
    /// <summary>
    /// Shared Serilog setup for the API and the workers. Levels come from the "Serilog"
    /// configuration section. Development gets readable console text; every other
    /// environment writes one JSON object per line so log shippers can parse it.
    /// </summary>
    public static IServiceCollection AddStatusTideLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        return services.AddSerilog(logger =>
        {
            logger
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", environment.ApplicationName)
                .Enrich.WithProperty("Environment", environment.EnvironmentName);

            if (environment.IsDevelopment())
            {
                logger.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });
    }
}
