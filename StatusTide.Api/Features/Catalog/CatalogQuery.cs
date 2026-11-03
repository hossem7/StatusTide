using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Catalog;

/// <summary>
/// What a user can follow, and how much of it they already do. Only enabled catalog vendors
/// appear, so a vendor that was removed or a page someone else added can't be found here.
/// </summary>
public class CatalogQuery(StatusTideDbContext db)
{
    private const char LikeEscape = '\\';

    /// <summary>Matching vendors by name. The catalog is short and curated, so there are no pages.</summary>
    public async Task<CatalogDto> GetAsync(Guid userId, string? search, VendorCategory? category, CancellationToken cancellationToken)
    {
        var vendors = Followable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            vendors = vendors.Where(v => EF.Functions.ILike(v.Name, pattern, LikeEscape.ToString()));
        }

        if (category is { } wanted)
        {
            vendors = vendors.Where(v => v.Category == wanted);
        }

        var rows = await vendors
            .OrderBy(v => v.Name)
            .Select(v => new CatalogVendorDto(
                v.Id,
                v.Slug,
                v.Name,
                v.Category,
                v.StatusPageUrl,
                v.Components.Count,
                db.Follows.Count(f => f.UserId == userId && f.Component!.VendorId == v.Id)))
            .ToListAsync(cancellationToken);
        return new CatalogDto(rows);
    }

    /// <summary>One vendor for the component picker, or null when it can't be followed.</summary>
    public async Task<CatalogVendorDetailDto?> GetVendorAsync(Guid userId, Guid vendorId, CancellationToken cancellationToken)
    {
        var vendor = await Followable()
            .Where(v => v.Id == vendorId)
            .Select(v => new { v.Id, v.Name, v.StatusPageUrl })
            .SingleOrDefaultAsync(cancellationToken);
        if (vendor is null)
        {
            return null;
        }

        var components = await db.Components
            .AsNoTracking()
            .Where(c => c.VendorId == vendorId)
            .OrderBy(c => c.Name)
            .Select(c => new CatalogComponentDto(c.Id, c.Name, c.Status, db.Follows.Any(f => f.UserId == userId && f.ComponentId == c.Id)))
            .ToListAsync(cancellationToken);
        return new CatalogVendorDetailDto(vendor.Id, vendor.Name, vendor.StatusPageUrl, components);
    }

    private IQueryable<Vendor> Followable() => db.Vendors.AsNoTracking().Where(v => v.IsCatalog && v.IsEnabled);

    /// <summary>A search for "100%" means those characters, not a wildcard.</summary>
    private static string EscapeLike(string text) => text
        .Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal)
        .Replace("_", @"\_", StringComparison.Ordinal);
}

public record CatalogDto(List<CatalogVendorDto> Vendors);

/// <param name="FollowedComponentCount">How many of its components this user follows. Zero means not following.</param>
public record CatalogVendorDto(
    Guid Id,
    string Slug,
    string Name,
    VendorCategory? Category,
    string StatusPageUrl,
    int ComponentCount,
    int FollowedComponentCount);

public record CatalogVendorDetailDto(Guid Id, string Name, string StatusPageUrl, List<CatalogComponentDto> Components);

public record CatalogComponentDto(Guid Id, string Name, ComponentStatus Status, bool Followed);
