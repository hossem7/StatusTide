using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StatusTide.Common.Logging;

namespace StatusTide.UnitTests.Logging;

public class LoggingExtensionsTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Registers_a_working_logger_in_every_environment(string environmentName)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Warning",
        });

        builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<LoggingExtensionsTests>>();
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.Information));
    }
}
