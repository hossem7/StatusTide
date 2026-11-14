using System.Text.Json;
using System.Text.Json.Serialization;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling.Slack;

/// <summary>
/// Reads Slack's status API (slack-status.com/api/v2.0.0/current). The feed only lists active
/// incidents and the services they hit, so every documented service is always a component and
/// counts as operational unless an active incident names it.
/// </summary>
public class SlackReader(FeedClient feeds) : IStatusReader
{
    /// <summary>The services Slack documents for the current endpoint. Their names are the component ids.</summary>
    public static readonly IReadOnlyList<string> Services =
    [
        "Login/SSO",
        "Messaging",
        "Notifications",
        "Search",
        "Workspace/Org Administration",
        "Canvases",
        "Connectivity",
        "Files",
        "Huddles",
        "Apps/Integrations/APIs",
        "Workflows",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowTrailingCommas = true,
        Converters = { new StringOrNumberConverter() },
    };

    public FeedFormat Format => FeedFormat.Slack;

    public async Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken)
    {
        var response = await feeds.GetAsync(feedUrl, validators, cancellationToken);
        if (response.Body is not { } body)
        {
            return new StatusReadResult(null, response.Validators);
        }

        SlackCurrent? current;
        try
        {
            current = JsonSerializer.Deserialize<SlackCurrent>(body, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new StatusReadException(StatusReadFailure.Malformed, "Feed is not valid Slack status JSON.", e);
        }

        if (current?.Status is null)
        {
            throw new StatusReadException(StatusReadFailure.Malformed, "Feed has no status field.");
        }

        var incidents = (current.ActiveIncidents ?? [])
            .Select(i => MapIncident(i, feedUrl))
            .OfType<(IncidentSnapshot Incident, ComponentStatus Impact)>()
            .ToList();

        return new StatusReadResult(new StatusSnapshot(MapComponents(incidents), incidents.Select(i => i.Incident).ToList()), response.Validators);
    }

    /// <summary>
    /// Every documented service, plus any new one an incident names. Each takes the worst level
    /// among the open incidents that name it.
    /// </summary>
    private static List<ComponentSnapshot> MapComponents(List<(IncidentSnapshot Incident, ComponentStatus Impact)> incidents)
    {
        var statuses = Services.ToDictionary(s => s, _ => ComponentStatus.Operational);
        var order = Services.ToList();

        foreach (var (incident, impact) in incidents)
        {
            foreach (var service in incident.ComponentExternalIds)
            {
                if (!statuses.TryGetValue(service, out var status))
                {
                    status = ComponentStatus.Operational;
                    order.Add(service);
                }

                statuses[service] = incident.Status == IncidentStatus.Resolved ? status : FeedText.Worse(status, impact);
            }
        }

        return order.Select(s => new ComponentSnapshot(s, s, statuses[s])).ToList();
    }

    private static (IncidentSnapshot, ComponentStatus)? MapIncident(SlackIncident incident, Uri feedUrl)
    {
        var id = FeedText.Clean(incident.Id, FeedText.MaxIdLength);
        if (id is null || incident.DateCreated is not { } startedAt)
        {
            return null;
        }

        // Scheduled, completed and cancelled entries are planned maintenance, not incidents.
        var resolved = incident.Status switch
        {
            "active" => false,
            "resolved" => true,
            _ => (bool?)null,
        };
        if (resolved is null)
        {
            return null;
        }

        var resolvedAt = resolved.Value ? incident.DateUpdated ?? startedAt : (DateTimeOffset?)null;

        // Notes carry the update text, so only their times are kept. Slack gives a note no
        // status of its own, so every note counts as investigating until the incident resolves.
        var updates = (incident.Notes ?? [])
            .Where(n => n.DateCreated is not null)
            .Select(n => new IncidentUpdateSnapshot(IncidentStatus.Investigating, n.DateCreated!.Value))
            .ToList();
        if (resolvedAt is { } at)
        {
            updates.Add(new IncidentUpdateSnapshot(IncidentStatus.Resolved, at));
        }

        var services = (incident.Services ?? [])
            .Select(s => FeedText.Clean(s, FeedText.MaxNameLength))
            .OfType<string>()
            .Distinct()
            .ToList();

        var snapshot = new IncidentSnapshot(
            id,
            FeedText.Clean(incident.Title, FeedText.MaxTitleLength) ?? "Untitled incident",
            resolved.Value ? IncidentStatus.Resolved : IncidentStatus.Investigating,
            startedAt,
            resolvedAt,
            FeedText.HttpUrl(incident.Url) ?? new Uri(feedUrl, "/").AbsoluteUri,
            services,
            updates.OrderBy(u => u.OccurredAt).ToList());

        return (snapshot, Impact(incident.Type));
    }

    // Slack's three kinds, from most to least serious. An unknown kind still affects the service.
    private static ComponentStatus Impact(string? type) => type switch
    {
        "outage" => ComponentStatus.MajorOutage,
        "incident" => ComponentStatus.PartialOutage,
        _ => ComponentStatus.DegradedPerformance,
    };

    private sealed record SlackCurrent(string? Status, List<SlackIncident>? ActiveIncidents);

    private sealed record SlackIncident(
        string? Id,
        DateTimeOffset? DateCreated,
        DateTimeOffset? DateUpdated,
        string? Title,
        string? Type,
        string? Status,
        string? Url,
        List<string?>? Services,
        List<SlackNote>? Notes);

    // The note body is the update text and is never read.
    private sealed record SlackNote(DateTimeOffset? DateCreated);
}
