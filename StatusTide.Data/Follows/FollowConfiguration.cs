using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatusTide.Data.Follows;

internal class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> follow)
    {
        // Also serves the dashboard query (a user's follows), since user_id leads the index.
        follow.HasIndex(f => new { f.UserId, f.ComponentId }).IsUnique();

        // Alerts start from a component that changed and look up who follows it.
        follow.HasIndex(f => f.ComponentId);
    }
}
