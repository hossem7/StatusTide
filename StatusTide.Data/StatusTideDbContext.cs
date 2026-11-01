using Microsoft.EntityFrameworkCore;

using StatusTide.Data.Alerts;
using StatusTide.Data.Checks;
using StatusTide.Data.Follows;
using StatusTide.Data.Incidents;
using StatusTide.Data.Seed;
using StatusTide.Data.Users;
using StatusTide.Data.Vendors;

namespace StatusTide.Data;

/// <summary>
/// The one PostgreSQL database. Table and column names are snake_case (see
/// <see cref="DataExtensions"/>), and each feature folder holds its own entity configurations.
/// </summary>
public class StatusTideDbContext(DbContextOptions<StatusTideDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<VendorVerification> VendorVerifications => Set<VendorVerification>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentEvent> IncidentEvents => Set<IncidentEvent>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<CheckRollup> CheckRollups => Set<CheckRollup>();
    public DbSet<AlertDelivery> AlertDeliveries => Set<AlertDelivery>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums as text so the database stays readable and reordering an enum
        // can never change the meaning of existing rows.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(50);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StatusTideDbContext).Assembly);
        AcmeCloudSeed.Apply(modelBuilder);
    }
}
