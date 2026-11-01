using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatusTide.Data.Users;

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> user)
    {
        user.Property(u => u.Email).HasMaxLength(320);
        user.HasIndex(u => u.Email).IsUnique();

        // Every guest request looks the user up by this hash.
        user.Property(u => u.GuestTokenHash).HasMaxLength(32);
        user.HasIndex(u => u.GuestTokenHash).IsUnique().HasFilter("guest_token_hash IS NOT NULL");

        // The guest cleanup job only looks for old guests, so accounts stay out of the index.
        user.HasIndex(u => u.LastSeenAt).HasFilter("is_guest");

        user.Property(u => u.PasswordHash).HasMaxLength(256);
        user.Property(u => u.EmailConfirmationHash).HasMaxLength(32);
        user.HasIndex(u => u.EmailConfirmationHash).IsUnique().HasFilter("email_confirmation_hash IS NOT NULL");
    }
}

internal class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> token)
    {
        token.Property(t => t.TokenHash).HasMaxLength(32);
        token.HasIndex(t => t.TokenHash).IsUnique();
        token.HasIndex(t => t.FamilyId);
        token.HasIndex(t => t.UserId);
        token.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
