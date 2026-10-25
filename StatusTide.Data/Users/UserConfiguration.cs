using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatusTide.Data.Users;

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> user)
    {
        user.Property(u => u.Email).HasMaxLength(320);
        user.HasIndex(u => u.Email).IsUnique();

        // The guest cleanup job only looks for old guests, so accounts stay out of the index.
        user.HasIndex(u => u.LastSeenAt).HasFilter("is_guest");
    }
}
