using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StatusTide.Data;

/// <summary>
/// Lets `dotnet ef migrations add` build the context without starting an app.
/// Adding a migration never connects, so the connection string is a placeholder.
/// </summary>
internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StatusTideDbContext>
{
    public StatusTideDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StatusTideDbContext>()
            .UseNpgsql("Host=localhost;Database=statustide")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new StatusTideDbContext(options);
    }
}
