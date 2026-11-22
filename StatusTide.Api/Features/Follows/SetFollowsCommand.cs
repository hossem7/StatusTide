using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Follows;

namespace StatusTide.Api.Features.Follows;

public enum SetFollowsOutcome
{
    Saved,
    NotSignedIn,
    VendorNotFound,
    UnknownComponents,
    LimitReached,
}

/// <summary>
/// Sets which of a vendor's components a user follows. Kept follows stay as they are, so choosing
/// different components never resets the alert settings of the ones still chosen.
/// </summary>
public class SetFollowsCommand(StatusTideDbContext db, TimeProvider timeProvider)
{
    /// <summary>Different vendors a guest can follow. Each one adds to the work of building their dashboard.</summary>
    public const int GuestVendorLimit = 5;

    /// <summary>Different vendors an account can follow.</summary>
    public const int AccountVendorLimit = 25;

    /// <param name="componentIds">The components to follow, or null for all of the vendor's components.</param>
    public async Task<SetFollowsOutcome> ExecuteAsync(
        Guid userId,
        Guid vendorId,
        IReadOnlyCollection<Guid>? componentIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Holds this user's other follow changes until this one commits. Without it, two requests
        // at once could each see room under the limit and both add a vendor.
        var user = (await db.Users
            .FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is null)
        {
            return SetFollowsOutcome.NotSignedIn;
        }

        // Catalog vendors, plus custom pages this user already follows so they can choose components.
        // A custom page is first followed through AddCustomPageCommand, which checks its own limit.
        var vendorExists = await db.Vendors.AnyAsync(
            v => v.Id == vendorId && v.IsEnabled
                && (v.IsCatalog || db.Follows.Any(f => f.UserId == userId && f.Component!.VendorId == v.Id)),
            cancellationToken);
        if (!vendorExists)
        {
            return SetFollowsOutcome.VendorNotFound;
        }

        var vendorComponents = await db.Components
            .Where(c => c.VendorId == vendorId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        var wanted = (componentIds ?? vendorComponents).ToHashSet();
        if (!wanted.IsSubsetOf(vendorComponents))
        {
            return SetFollowsOutcome.UnknownComponents;
        }

        var followedVendors = await db.Follows
            .Where(f => f.UserId == userId)
            .Select(f => f.Component!.VendorId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var limit = user.IsGuest ? GuestVendorLimit : AccountVendorLimit;
        if (!followedVendors.Contains(vendorId) && followedVendors.Count >= limit)
        {
            return SetFollowsOutcome.LimitReached;
        }

        var existing = await db.Follows
            .Where(f => f.UserId == userId && f.Component!.VendorId == vendorId)
            .ToListAsync(cancellationToken);
        db.Follows.RemoveRange(existing.Where(f => !wanted.Contains(f.ComponentId)));

        var now = timeProvider.GetUtcNow();
        var kept = existing.Select(f => f.ComponentId).ToHashSet();
        db.Follows.AddRange(wanted
            .Where(id => !kept.Contains(id))
            .Select(id => new Follow { UserId = userId, ComponentId = id, CreatedAt = now }));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SetFollowsOutcome.Saved;
    }

    /// <summary>
    /// Unfollows a vendor. Works for a vendor that has since left the catalog, since people must
    /// always be able to stop following something. Only ever touches this user's rows. A custom
    /// page that nobody follows any more stops being polled; adding it again switches it back on.
    /// </summary>
    public async Task UnfollowAsync(Guid userId, Guid vendorId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Same lock as AddCustomPageCommand, so a follow can't be added between the count and the switch-off.
        var vendor = (await db.Vendors
            .FromSql($"SELECT * FROM vendors WHERE id = {vendorId} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

        await db.Follows
            .Where(f => f.UserId == userId && f.Component!.VendorId == vendorId)
            .ExecuteDeleteAsync(cancellationToken);

        if (vendor is { IsCatalog: false, IsEnabled: true }
            && !await db.Follows.AnyAsync(f => f.Component!.VendorId == vendorId, cancellationToken))
        {
            vendor.IsEnabled = false;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
