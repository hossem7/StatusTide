using Microsoft.EntityFrameworkCore;
using StatusTide.Data;
using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Incidents;

/// <summary>
/// Builds the incident page. Read only. Our checks are matched to the incident by time, since
/// they watch the whole vendor while the vendor's incident names components.
/// </summary>
public class IncidentQuery(StatusTideDbContext db, TimeProvider timeProvider)
{
    /// <summary>
    /// How long before the vendor's first post an outage of ours may start and still count as
    /// the same problem. Vendors often post well after their users notice.
    /// </summary>
    public static readonly TimeSpan MatchBefore = TimeSpan.FromHours(1);

    /// <summary>Extra time shown on each side of the incident in the chart.</summary>
    public static readonly TimeSpan ChartMargin = TimeSpan.FromMinutes(10);

    /// <summary>The longest stretch the chart covers, from its start. One bar a minute stays readable up to here.</summary>
    public static readonly TimeSpan ChartLength = TimeSpan.FromHours(3);

    /// <summary>
    /// The incident, or null when it doesn't exist or the caller may not see it. Catalog
    /// vendors are public; anything else (custom pages, from #16) only shows to its followers.
    /// </summary>
    public async Task<IncidentDto?> GetAsync(Guid userId, Guid incidentId, CancellationToken cancellationToken)
    {
        var incident = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Status,
                i.StartedAt,
                i.ResolvedAt,
                i.SourceUrl,
                i.VendorId,
                i.Vendor!.Slug,
                VendorName = i.Vendor.Name,
                i.Vendor.Format,
                i.Vendor.CheckUrl,
                i.Vendor.IsCatalog,
                AffectedIds = i.Components.Select(c => c.Id).ToList(),
                Updates = i.Events
                    .Where(e => e.Kind == IncidentEventKind.VendorUpdate && e.VendorStatus != null)
                    .OrderByDescending(e => e.OccurredAt)
                    .Select(e => new VendorUpdateDto(e.VendorStatus!.Value, e.OccurredAt))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (incident is null)
        {
            return null;
        }

        var followedIds = await db.Follows
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.Component!.VendorId == incident.VendorId)
            .Select(f => f.ComponentId)
            .ToListAsync(cancellationToken);
        if (!incident.IsCatalog && followedIds.Count == 0)
        {
            return null;
        }

        var shownIds = incident.AffectedIds.Concat(followedIds).ToList();
        var components = await db.Components
            .AsNoTracking()
            .Where(c => shownIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Status })
            .ToListAsync(cancellationToken);

        // The earliest update is the vendor's first post. Before any update is stored, the
        // incident's own start stands in.
        var postedAt = incident.Updates.Count > 0 ? incident.Updates.Min(u => u.OccurredAt) : incident.StartedAt;
        var checks = incident.CheckUrl is null
            ? null
            : await ChecksAsync(incident.VendorId, postedAt, incident.ResolvedAt, cancellationToken);

        return new IncidentDto(
            incident.Id,
            incident.Title,
            incident.Status,
            incident.StartedAt,
            incident.ResolvedAt,
            incident.SourceUrl,
            new IncidentVendorDto(incident.VendorId, incident.Slug, incident.VendorName, incident.Format == FeedFormat.Demo),
            [.. components
                .Select(c => new IncidentComponentDto(c.Id, c.Name, c.Status, incident.AffectedIds.Contains(c.Id), followedIds.Contains(c.Id)))
                .OrderByDescending(c => c.IsAffected)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)],
            incident.Updates,
            checks);
    }

    private async Task<IncidentChecksDto> ChecksAsync(
        Guid vendorId,
        DateTimeOffset postedAt,
        DateTimeOffset? resolvedAt,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var incidentEnd = resolvedAt ?? now;

        // The first outage of ours that overlaps the incident, counting from an hour before the
        // vendor's post. Served by the (vendor_id, detected_at) index.
        var matchFrom = postedAt - MatchBefore;
        var outage = await db.CheckOutages
            .AsNoTracking()
            .Where(o => o.VendorId == vendorId
                && o.FirstFailedAt <= incidentEnd
                && (o.ResolvedAt == null || o.ResolvedAt >= matchFrom))
            .OrderBy(o => o.FirstFailedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var start = outage is null || postedAt < outage.FirstFailedAt ? postedAt : outage.FirstFailedAt;
        var end = Later(incidentEnd, outage?.ResolvedAt);
        var from = start - ChartMargin;
        var to = Earlier(Earlier(end + ChartMargin, now), from + ChartLength);

        var points = await db.Checks
            .AsNoTracking()
            .Where(c => c.VendorId == vendorId && c.CheckedAt >= from && c.CheckedAt <= to)
            .OrderBy(c => c.CheckedAt)
            .Select(c => new CheckPointDto(c.CheckedAt, c.Succeeded, c.StatusCode, c.ResponseTimeMs))
            .ToListAsync(cancellationToken);

        var detection = outage is null
            ? null
            : new EarlyDetectionDto(
                outage.FirstFailedAt,
                outage.DetectedAt,
                outage.ResolvedAt,
                postedAt,
                (int)Math.Round((postedAt - outage.DetectedAt).TotalSeconds));

        return new IncidentChecksDto(from, to, points, detection);
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset? b) => b is { } value && value > a ? value : a;

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
