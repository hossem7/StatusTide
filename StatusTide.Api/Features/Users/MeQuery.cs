using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Users;

/// <summary>The signed-in user, as the app header and the guest banner need it.</summary>
public class MeQuery(StatusTideDbContext db)
{
    public async Task<MeDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : MeDto.From(user);
    }
}

/// <param name="GuestExpiresAt">When a guest is deleted if they don't come back. Null for accounts.</param>
public record MeDto(Guid Id, bool IsGuest, string? Email, DateTimeOffset? GuestExpiresAt)
{
    public static MeDto From(User user) =>
        new(user.Id, user.IsGuest, user.Email, user.IsGuest ? user.LastSeenAt + GuestPolicy.Lifetime : null);
}
