using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Follows;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Guests;

/// <summary>
/// Creates a guest and follows every component of the starter vendors, so the first dashboard
/// is never empty. Starter vendors come from <c>Guests:StarterVendors</c> (slugs); missing,
/// disabled and non-catalog ones are skipped, since guests may only follow catalog vendors.
/// </summary>
public class StartGuestCommand(StatusTideDbContext db, IConfiguration configuration, TimeProvider timeProvider)
{
    public async Task<(User Guest, string Token)> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (token, hash) = GuestToken.Create();
        var guest = new User { IsGuest = true, GuestTokenHash = hash, CreatedAt = now, LastSeenAt = now };
        db.Users.Add(guest);

        var slugs = configuration.GetSection("Guests:StarterVendors").Get<string[]>() ?? [];
        var componentIds = await db.Components
            .Where(c => slugs.Contains(c.Vendor!.Slug) && c.Vendor.IsEnabled && c.Vendor.IsCatalog)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        db.Follows.AddRange(componentIds.Select(id => new Follow { UserId = guest.Id, ComponentId = id, CreatedAt = now }));

        await db.SaveChangesAsync(cancellationToken);
        return (guest, token);
    }
}
