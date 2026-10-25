using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StatusTide.Data;
using StatusTide.Data.Seed;

namespace StatusTide.IntegrationTests.Data;

public sealed class StartupMigrationTests(PostgresFixture postgres) : IAsyncDisposable
{
    // A fresh database, so only the API's startup can have migrated it.
    private readonly ApiFactory _factory = new(postgres, "startup_migration");

    [Fact]
    public async Task Api_startup_applies_all_migrations_and_the_demo_seed()
    {
        var ct = TestContext.Current.CancellationToken;
        _ = _factory.Services;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
        Assert.True(await db.Vendors.AnyAsync(v => v.Id == AcmeCloudSeed.VendorId, ct));
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_the_migrations()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();

        // Fails when someone edits an entity and forgets `dotnet ef migrations add`.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
