using Microsoft.EntityFrameworkCore;

using StatusTide.Contracts.CustomPages;
using StatusTide.Data;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.CustomPages;

/// <summary>The custom pages a user follows, and what a detected feed would add.</summary>
public class CustomPagesQuery(StatusTideDbContext db)
{
    public async Task<CustomPagesDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var pages = await db.Vendors
            .AsNoTracking()
            .Where(v => !v.IsCatalog && v.Components.Any(c => db.Follows.Any(f => f.UserId == userId && f.ComponentId == c.Id)))
            .OrderBy(v => v.Name)
            .Select(v => new CustomPageDto(
                v.Id,
                v.Name,
                v.FeedUrl,
                v.StatusPageUrl,
                v.Format,
                v.Components.Count,
                db.Follows.Count(f => f.UserId == userId && f.Component!.VendorId == v.Id)))
            .ToListAsync(cancellationToken);
        return new CustomPagesDto(pages, AddCustomPageCommand.CustomPageLimit);
    }

    /// <summary>
    /// The preview of <paramref name="feed"/>, and whether it is already in the catalog or followed,
    /// so the screen can send people to the right place before they save.
    /// </summary>
    public async Task<CustomPagePreviewDto> PreviewAsync(Guid userId, FeedDetected feed, CancellationToken cancellationToken)
    {
        var existing = await db.Vendors
            .AsNoTracking()
            .Where(v => v.FeedUrl == feed.FeedUrl)
            .Select(v => new
            {
                v.Id,
                v.IsCatalog,
                v.IsEnabled,
                Following = db.Follows.Any(f => f.UserId == userId && f.Component!.VendorId == v.Id),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new CustomPagePreviewDto(
            feed.FeedUrl,
            feed.StatusPageUrl,
            new Uri(feed.FeedUrl).Host,
            Enum.Parse<FeedFormat>(feed.Format),
            [.. feed.Components.Select(c => new CustomPageComponentDto(
                c.Name,
                Enum.TryParse<ComponentStatus>(c.Status, out var status) ? status : ComponentStatus.Unknown))],
            existing is { IsCatalog: true, IsEnabled: true } ? existing.Id : null,
            existing is { Following: true, IsCatalog: false } ? existing.Id : null);
    }
}

/// <param name="Limit">How many custom pages one account can follow.</param>
public record CustomPagesDto(List<CustomPageDto> Pages, int Limit);

public record CustomPageDto(
    Guid Id,
    string Name,
    string FeedUrl,
    string StatusPageUrl,
    FeedFormat Format,
    int ComponentCount,
    int FollowedComponentCount);

/// <param name="CatalogVendorId">Set when this feed is already in the catalog; follow it there instead.</param>
/// <param name="FollowingVendorId">Set when this user already follows this page.</param>
public record CustomPagePreviewDto(
    string FeedUrl,
    string StatusPageUrl,
    string Name,
    FeedFormat Format,
    List<CustomPageComponentDto> Components,
    Guid? CatalogVendorId,
    Guid? FollowingVendorId);

public record CustomPageComponentDto(string Name, ComponentStatus Status);
