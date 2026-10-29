using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Publisher.Features.Guests;

/// <summary>
/// Deletes guests inactive for <see cref="GuestPolicy.Lifetime"/>. Their follows and alert
/// deliveries go with them through cascading deletes. Accounts are never touched.
/// </summary>
public class GuestCleanup(StatusTideDbContext db, TimeProvider timeProvider, ILogger<GuestCleanup> logger)
{
    public async Task<int> DeleteInactiveGuestsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - GuestPolicy.Lifetime;
        var deleted = await db.Users
            .Where(u => u.IsGuest && u.LastSeenAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Deleted {Count} guests inactive since {Cutoff}", deleted, cutoff);
        return deleted;
    }
}
