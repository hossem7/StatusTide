using Microsoft.EntityFrameworkCore;

using Npgsql;

using StatusTide.Data;

using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(StatusTide.IntegrationTests.PostgresFixture))]

namespace StatusTide.IntegrationTests;

/// <summary>
/// One real PostgreSQL container for the whole test run, the same major version as Compose.
/// Tests ask for a database by name; each name gets its own database inside the container.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext("statustide");
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>Connection string for a database in the container. EF Core creates it on first migration.</summary>
    public string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    /// <summary>A context on the shared, already migrated "statustide" database.</summary>
    public StatusTideDbContext CreateDbContext() => CreateDbContext("statustide");

    private StatusTideDbContext CreateDbContext(string database)
    {
        var options = new DbContextOptionsBuilder<StatusTideDbContext>()
            .UseNpgsql(ConnectionString(database))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new StatusTideDbContext(options);
    }
}
