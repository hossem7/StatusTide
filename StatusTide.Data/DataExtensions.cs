using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace StatusTide.Data;

public static class DataExtensions
{
    /// <summary>
    /// Registers <see cref="StatusTideDbContext"/> on PostgreSQL, using the "Postgres"
    /// connection string. Names are snake_case to match PostgreSQL conventions.
    /// </summary>
    public static IServiceCollection AddStatusTideData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        }

        // We never use Kerberos. Without this, Npgsql tries to load libgssapi_krb5, which the
        // slim ASP.NET image does not ship, and logs an error on every new connection.
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable };

        return services.AddDbContext<StatusTideDbContext>(options => options
            .UseNpgsql(builder.ConnectionString)
            .UseSnakeCaseNamingConvention());
    }

    /// <summary>
    /// Applies pending migrations. Used on startup in development and in the local stacks
    /// only; production will run migrations as a separate deploy step.
    /// </summary>
    public static async Task MigrateStatusTideDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
