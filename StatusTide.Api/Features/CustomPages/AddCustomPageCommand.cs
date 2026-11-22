using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using StatusTide.Api.Features.Follows;
using StatusTide.Contracts.CustomPages;
using StatusTide.Data;
using StatusTide.Data.Follows;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.CustomPages;

public enum AddCustomPageOutcome
{
    Added,
    NotAnAccount,
    InCatalog,
    CustomLimitReached,
    LimitReached,
}

public record AddCustomPageResult(AddCustomPageOutcome Outcome, Guid? VendorId = null);

/// <summary>
/// Adds a status page an account found with <see cref="DetectFeed"/>, and follows all of it.
/// A page is one vendor row per feed URL, so when someone already added the same feed this
/// follows that row (switching its polling back on if nobody followed it) instead of adding another.
/// </summary>
public class AddCustomPageCommand(StatusTideDbContext db, TimeProvider timeProvider)
{
    /// <summary>Custom pages one account can follow. They also count toward <see cref="SetFollowsCommand.AccountVendorLimit"/>.</summary>
    public const int CustomPageLimit = 10;

    /// <summary>Feeds that aren't in the catalog are polled every 5 minutes, as the plan sets for feeds.</summary>
    public const int PollIntervalSeconds = 300;

    public async Task<AddCustomPageResult> ExecuteAsync(Guid userId, FeedDetected feed, CancellationToken cancellationToken)
    {
        try
        {
            return await TryExecuteAsync(userId, feed, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone else added the same feed at the same moment. Their row exists now, so follow it.
            db.ChangeTracker.Clear();
            return await TryExecuteAsync(userId, feed, cancellationToken);
        }
    }

    private async Task<AddCustomPageResult> TryExecuteAsync(Guid userId, FeedDetected feed, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Same lock as SetFollowsCommand, so two requests can't both pass the limits.
        var user = (await db.Users
            .FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is not { IsGuest: false })
        {
            return new AddCustomPageResult(AddCustomPageOutcome.NotAnAccount);
        }

        // Locked so the last follower leaving can't switch polling off while this adds a follow.
        var vendor = (await db.Vendors
            .FromSql($"SELECT * FROM vendors WHERE feed_url = {feed.FeedUrl} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (vendor is { IsCatalog: true })
        {
            return new AddCustomPageResult(AddCustomPageOutcome.InCatalog, vendor.Id);
        }

        // Loaded on its own: a FOR UPDATE query can't be joined.
        if (vendor is not null)
        {
            await db.Entry(vendor).Collection(v => v.Components).LoadAsync(cancellationToken);
        }

        var followed = await db.Follows
            .Where(f => f.UserId == userId)
            .Select(f => new { f.Component!.VendorId, f.Component.Vendor!.IsCatalog })
            .Distinct()
            .ToListAsync(cancellationToken);
        if (vendor is null || followed.All(f => f.VendorId != vendor.Id))
        {
            if (followed.Count(f => !f.IsCatalog) >= CustomPageLimit)
            {
                return new AddCustomPageResult(AddCustomPageOutcome.CustomLimitReached);
            }

            if (followed.Count >= SetFollowsCommand.AccountVendorLimit)
            {
                return new AddCustomPageResult(AddCustomPageOutcome.LimitReached);
            }
        }

        var now = timeProvider.GetUtcNow();
        if (vendor is null)
        {
            vendor = NewVendor(feed, now);
            db.Vendors.Add(vendor);
            db.Components.AddRange(vendor.Components);
        }
        else if (!vendor.IsEnabled)
        {
            // Nobody followed it for a while, so its statuses are old. Poll it straight away.
            vendor.IsEnabled = true;
            vendor.NextPollAt = null;
        }

        var already = await db.Follows
            .Where(f => f.UserId == userId && f.Component!.VendorId == vendor.Id)
            .Select(f => f.ComponentId)
            .ToListAsync(cancellationToken);
        db.Follows.AddRange(vendor.Components
            .Where(c => !already.Contains(c.Id))
            .Select(c => new Follow { UserId = userId, ComponentId = c.Id, CreatedAt = now }));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AddCustomPageResult(AddCustomPageOutcome.Added, vendor.Id);
    }

    /// <summary>
    /// Named after its host rather than anything the page says or the person types, since every
    /// account that adds the same feed sees the same row.
    /// </summary>
    private static Vendor NewVendor(FeedDetected feed, DateTimeOffset now)
    {
        var feedUrl = new Uri(feed.FeedUrl);
        var vendor = new Vendor
        {
            Slug = Slug(feed.FeedUrl),
            Name = feedUrl.Host,
            FeedUrl = feed.FeedUrl,
            StatusPageUrl = feed.StatusPageUrl,
            Format = Enum.Parse<FeedFormat>(feed.Format),
            IsCatalog = false,
            IsEnabled = true,
            PollIntervalSeconds = PollIntervalSeconds,
            CreatedAt = now,
        };

        vendor.Components.AddRange(feed.Components.Select(c => new Component
        {
            VendorId = vendor.Id,
            ExternalId = c.ExternalId,
            Name = c.Name,
            Status = Enum.TryParse<ComponentStatus>(c.Status, out var status) ? status : ComponentStatus.Unknown,
            StatusChangedAt = now,
        }));
        return vendor;
    }

    /// <summary>Fixed for a feed URL, so it never collides with a catalog slug or changes between tries.</summary>
    public static string Slug(string feedUrl) =>
        "custom-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(feedUrl)))[..16];
}
