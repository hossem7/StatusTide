using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatusTide.Data.Checks;

internal class CheckConfiguration : IEntityTypeConfiguration<Check>
{
    public void Configure(EntityTypeBuilder<Check> check)
    {
        check.Property(c => c.TargetUrl).HasMaxLength(2048);

        // Recent checks for one vendor (incident chart, failure streaks).
        check.HasIndex(c => new { c.VendorId, c.CheckedAt });

        // The retention job deletes rows older than 7 days across all vendors.
        check.HasIndex(c => c.CheckedAt);
    }
}

internal class CheckRollupConfiguration : IEntityTypeConfiguration<CheckRollup>
{
    public void Configure(EntityTypeBuilder<CheckRollup> rollup)
    {
        // One row per vendor per hour, so re-running the rollup job updates instead of duplicating.
        rollup.HasKey(r => new { r.VendorId, r.HourStart });
        rollup.Property(r => r.UptimePercent).HasPrecision(5, 2);
    }
}
