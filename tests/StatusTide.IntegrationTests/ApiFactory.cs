using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StatusTide.IntegrationTests;

/// <summary>
/// Runs the real API in memory against its own database in the test container,
/// with migrations on startup, the same as local development.
/// </summary>
public sealed class ApiFactory(PostgresFixture postgres, string database) : WebApplicationFactory<Program>
{
    public string ConnectionString { get; } = postgres.ConnectionString(database);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }
}
