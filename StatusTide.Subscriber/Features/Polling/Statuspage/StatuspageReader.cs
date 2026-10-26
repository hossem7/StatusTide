using System.Text.Json;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling.Statuspage;

/// <summary>
/// Reads Atlassian Statuspage's /api/v2/summary.json: components and unresolved incidents.
/// Everything in the feed is untrusted, so bad entries are skipped or cut to our column
/// sizes instead of failing the whole poll.
/// </summary>
public class StatuspageReader(FeedClient feeds) : IStatusReader
{
    // Match the column sizes in StatusTide.Data.
    private const int MaxIdLength = 200;
    private const int MaxNameLength = 200;
    private const int MaxTitleLength = 500;
    private const int MaxUrlLength = 2048;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public FeedFormat Format => FeedFormat.Statuspage;

    public async Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken)
    {
        var response = await feeds.GetAsync(feedUrl, validators, cancellationToken);
        if (response.Body is not { } body)
        {
            return new StatusReadResult(null, response.Validators);
        }

        StatuspageSummary? summary;
        try
        {
            summary = JsonSerializer.Deserialize<StatuspageSummary>(body, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new StatusReadException(StatusReadFailure.Malformed, "Feed is not valid Statuspage JSON.", e);
        }

        if (summary?.Components is null)
        {
            throw new StatusReadException(StatusReadFailure.Malformed, "Feed has no components list.");
        }

        var components = MapComponents(summary.Components);
        var componentIds = components.Select(c => c.ExternalId).ToHashSet();
        var incidents = (summary.Incidents ?? [])
            .Select(i => MapIncident(i, feedUrl, componentIds))
            .OfType<IncidentSnapshot>()
            .ToList();

        return new StatusReadResult(new StatusSnapshot(components, incidents), response.Validators);
    }

    private static List<ComponentSnapshot> MapComponents(List<StatuspageComponent> components)
    {
        var mapped = new List<ComponentSnapshot>();
        var seen = new HashSet<string>();

        // Groups only hold other components and have no status of their own worth following.
        foreach (var component in components.Where(c => !c.Group))
        {
            var id = Clean(component.Id, MaxIdLength);
            var name = Clean(component.Name, MaxNameLength);
            if (id is null || name is null || !seen.Add(id))
            {
                continue;
            }

            mapped.Add(new ComponentSnapshot(id, name, MapComponentStatus(component.Status)));
        }

        return mapped;
    }

    private static IncidentSnapshot? MapIncident(StatuspageIncident incident, Uri feedUrl, HashSet<string> componentIds)
    {
        var id = Clean(incident.Id, MaxIdLength);
        var startedAt = incident.StartedAt ?? incident.CreatedAt;
        if (id is null || startedAt is null)
        {
            return null;
        }

        var updates = (incident.IncidentUpdates ?? [])
            .Where(u => (u.DisplayAt ?? u.CreatedAt) is not null)
            .Select(u => new IncidentUpdateSnapshot(MapIncidentStatus(u.Status), (u.DisplayAt ?? u.CreatedAt)!.Value))
            .OrderBy(u => u.OccurredAt)
            .ToList();

        // Affected components come from the incident and from each update. Ids we do not
        // know, such as groups, are dropped.
        var affected = (incident.Components ?? []).Select(c => c.Id)
            .Concat((incident.IncidentUpdates ?? []).SelectMany(u => u.AffectedComponents ?? []).Select(c => c.Code))
            .OfType<string>()
            .Where(componentIds.Contains)
            .Distinct()
            .ToList();

        return new IncidentSnapshot(
            id,
            Clean(incident.Name, MaxTitleLength) ?? "Untitled incident",
            incident.ResolvedAt is null ? MapIncidentStatus(incident.Status) : IncidentStatus.Resolved,
            startedAt.Value,
            incident.ResolvedAt,
            SourceUrl(incident.Shortlink, feedUrl, id),
            affected,
            updates);
    }

    /// <summary>
    /// The link users follow to read the full update. Only plain http or https links are
    /// kept; anything else falls back to the incident page on the verified feed's own host.
    /// </summary>
    private static string SourceUrl(string? shortlink, Uri feedUrl, string incidentId)
    {
        if (Uri.TryCreate(shortlink, UriKind.Absolute, out var link)
            && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            && link.AbsoluteUri.Length <= MaxUrlLength)
        {
            return link.AbsoluteUri;
        }

        return new Uri(feedUrl, "/incidents/" + Uri.EscapeDataString(incidentId)).AbsoluteUri;
    }

    private static ComponentStatus MapComponentStatus(string? status) => status switch
    {
        "operational" => ComponentStatus.Operational,
        "degraded_performance" => ComponentStatus.DegradedPerformance,
        "partial_outage" => ComponentStatus.PartialOutage,
        "major_outage" => ComponentStatus.MajorOutage,
        "under_maintenance" => ComponentStatus.UnderMaintenance,
        _ => ComponentStatus.Unknown,
    };

    // An unresolved incident with a status we do not recognize is still open, so it counts
    // as investigating rather than being hidden.
    private static IncidentStatus MapIncidentStatus(string? status) => status switch
    {
        "identified" => IncidentStatus.Identified,
        "monitoring" => IncidentStatus.Monitoring,
        "resolved" or "postmortem" => IncidentStatus.Resolved,
        _ => IncidentStatus.Investigating,
    };

    private static string? Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
