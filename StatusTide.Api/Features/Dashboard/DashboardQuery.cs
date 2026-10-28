using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StatusTide.Data;
using StatusTide.Data.Follows;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Dashboard;

/// <summary>
/// Builds vendor cards for the dashboard. The page load and the live updates both go through
/// <see cref="BuildCardsAsync"/>, so a card pushed over the hub always matches the one a reload
/// would show. Read only: nothing here is tracked or saved.
/// </summary>
public class DashboardQuery(StatusTideDbContext db, IConnectionMultiplexer redis, ILogger<DashboardQuery> logger)
{
    /// <summary>
    /// Set by the poll worker on every successful poll, so unchanged polls stay free of
    /// database writes. Holds the poll time as an ISO 8601 string.
    /// </summary>
    public static string PolledKey(Guid vendorId) => $"polled:vendor:{vendorId}";

    /// <summary>The user's dashboard, worst vendor first. Vendors with no followed components never appear.</summary>
    public async Task<DashboardDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var follows = await FollowsAsync(f => f.UserId == userId, cancellationToken);
        var cards = await BuildCardsAsync(follows, cancellationToken);
        return new DashboardDto(
            [.. cards
                .Select(c => c.Card)
                .OrderByDescending(c => VendorSeverity.Rank(c.Status))
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The card each follower of the vendor should see now, keyed by user id.</summary>
    public async Task<Dictionary<Guid, VendorCardDto>> GetCardsForFollowersAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var follows = await FollowsAsync(f => f.Component!.VendorId == vendorId, cancellationToken);
        var cards = await BuildCardsAsync(follows, cancellationToken);
        return cards.ToDictionary(c => c.UserId, c => c.Card);
    }

    private Task<List<FollowRow>> FollowsAsync(Expression<Func<Follow, bool>> filter, CancellationToken cancellationToken) => db.Follows
        .AsNoTracking()
        .Where(filter)
        .Select(f => new FollowRow(f.UserId, f.Component!.VendorId, f.ComponentId, f.Component.Name, f.Component.Status))
        .ToListAsync(cancellationToken);

    private async Task<List<(Guid UserId, VendorCardDto Card)>> BuildCardsAsync(List<FollowRow> follows, CancellationToken cancellationToken)
    {
        var vendorIds = follows.Select(f => f.VendorId).Distinct().ToList();
        if (vendorIds.Count == 0)
        {
            return [];
        }

        var vendors = await db.Vendors
            .AsNoTracking()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Slug, v.Name, v.Format, ComponentCount = v.Components.Count })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        // Served by the partial index on open incidents.
        var openIncidents = (await db.Incidents
            .AsNoTracking()
            .Where(i => vendorIds.Contains(i.VendorId) && i.ResolvedAt == null)
            .Select(i => new { i.Id, i.VendorId, i.Title, i.StartedAt })
            .ToListAsync(cancellationToken))
            .ToLookup(i => i.VendorId);

        var lastChecked = await ReadLastCheckedAsync(vendorIds);

        return [.. follows
            .GroupBy(f => (f.UserId, f.VendorId))
            .Select(group =>
            {
                var vendor = vendors[group.Key.VendorId];
                var incidents = openIncidents[vendor.Id].ToList();
                var latest = incidents.MaxBy(i => i.StartedAt);
                var components = group
                    .OrderBy(f => f.ComponentName, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new FollowedComponentDto(f.ComponentId, f.ComponentName, f.Status))
                    .ToList();

                var card = new VendorCardDto(
                    vendor.Id,
                    vendor.Slug,
                    vendor.Name,
                    VendorSeverity.Worst(components.Select(c => c.Status)),
                    components,
                    vendor.ComponentCount,
                    lastChecked.TryGetValue(vendor.Id, out var checkedAt) ? checkedAt : null,
                    vendor.Format == FeedFormat.Demo,
                    incidents.Count,
                    latest is null ? null : new ActiveIncidentDto(latest.Id, latest.Title, latest.StartedAt));
                return (group.Key.UserId, card);
            })];
    }

    /// <summary>
    /// Reads every vendor's last poll time in one round trip. Redis being down only hides
    /// "Checked 40s ago"; the rest of the dashboard still loads.
    /// </summary>
    private async Task<Dictionary<Guid, DateTimeOffset>> ReadLastCheckedAsync(List<Guid> vendorIds)
    {
        RedisValue[] values;
        try
        {
            values = await redis.GetDatabase().StringGetAsync([.. vendorIds.Select(id => (RedisKey)PolledKey(id))]);
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            logger.LogWarning(e, "Could not read last poll times for {Count} vendors", vendorIds.Count);
            return [];
        }

        var result = new Dictionary<Guid, DateTimeOffset>();
        for (var i = 0; i < vendorIds.Count; i++)
        {
            if (!values[i].IsNullOrEmpty
                && DateTimeOffset.TryParse(values[i].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var polledAt))
            {
                result[vendorIds[i]] = polledAt;
            }
        }

        return result;
    }

    private sealed record FollowRow(Guid UserId, Guid VendorId, Guid ComponentId, string ComponentName, ComponentStatus Status);
}
