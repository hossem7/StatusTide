using MassTransit;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace StatusTide.IntegrationTests;

/// <summary>
/// Runs the real API in memory against its own database in the test container,
/// with migrations on startup, the same as local development. The bus is the in-memory
/// test harness, so tests can see what the API publishes without a RabbitMQ.
/// </summary>
public sealed class ApiFactory(PostgresFixture postgres, string database, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public string ConnectionString { get; } = postgres.ConnectionString(database);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            configureServices?.Invoke(services);
        });
    }
}
