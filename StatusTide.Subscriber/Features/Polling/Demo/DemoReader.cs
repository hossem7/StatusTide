using StatusTide.Common.Demo;
using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling.Demo;

/// <summary>
/// Reads the demo vendor, Acme Cloud. There is no feed to fetch: the snapshot is built from the
/// simulated outage in <see cref="DemoOutageStore"/>, so it goes through the same change
/// detection, saving and announcing as a real vendor's feed.
/// </summary>
public class DemoReader(DemoOutageStore store, TimeProvider timeProvider) : IStatusReader
{
    /// <summary>The incident moves from investigating to identified after this long.</summary>
    public static readonly TimeSpan IdentifiedAfter = TimeSpan.FromSeconds(90);

    /// <summary>A fix is out and the incident is monitored after this long.</summary>
    public static readonly TimeSpan MonitoringAfter = TimeSpan.FromMinutes(3);

    private static readonly FeedValidators NoValidators = new(null, null);

    public FeedFormat Format => FeedFormat.Demo;

    public async Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken)
    {
        var outage = await store.GetAsync();
        return new StatusReadResult(Snapshot(outage, timeProvider.GetUtcNow()), NoValidators);
    }

    /// <summary>
    /// Acme Cloud's feed at <paramref name="now"/>. During an outage the API is down and webhooks,
    /// which depend on it, are slow. Once resolved, the incident stays in the feed until the
    /// cooldown ends, so it is saved with its real resolve time instead of our detection time.
    /// </summary>
    public static StatusSnapshot Snapshot(DemoOutage? outage, DateTimeOffset now)
    {
        if (outage is null || now < outage.StartedAt)
        {
            return new StatusSnapshot(Components(ComponentStatus.Operational, ComponentStatus.Operational), []);
        }

        var end = outage.IsActiveAt(now) ? now : outage.ResolvesAt;
        var updates = new List<IncidentUpdateSnapshot> { new(IncidentStatus.Investigating, outage.StartedAt) };
        if (end - outage.StartedAt >= IdentifiedAfter)
        {
            updates.Add(new(IncidentStatus.Identified, outage.StartedAt + IdentifiedAfter));
        }

        if (end - outage.StartedAt >= MonitoringAfter)
        {
            updates.Add(new(IncidentStatus.Monitoring, outage.StartedAt + MonitoringAfter));
        }

        if (!outage.IsActiveAt(now))
        {
            updates.Add(new(IncidentStatus.Resolved, outage.ResolvesAt));
        }

        var status = updates[^1].Status;
        var components = status switch
        {
            IncidentStatus.Resolved => Components(ComponentStatus.Operational, ComponentStatus.Operational),
            IncidentStatus.Monitoring => Components(ComponentStatus.DegradedPerformance, ComponentStatus.Operational),
            _ => Components(ComponentStatus.MajorOutage, ComponentStatus.DegradedPerformance),
        };

        // One incident per outage, named by its start time, so each simulation is a new incident.
        var incident = new IncidentSnapshot(
            $"demo-{outage.StartedAt.ToUnixTimeSeconds()}",
            "Elevated error rates on the API",
            status,
            outage.StartedAt,
            status == IncidentStatus.Resolved ? outage.ResolvesAt : null,
            "/vendors/acme-cloud",
            ["api", "webhooks"],
            updates);

        return new StatusSnapshot(components, [incident]);
    }

    // Ids and names match the seed in AcmeCloudSeed, so a quiet feed changes nothing.
    private static List<ComponentSnapshot> Components(ComponentStatus api, ComponentStatus webhooks) =>
    [
        new("api", "API", api),
        new("dashboard", "Dashboard", ComponentStatus.Operational),
        new("webhooks", "Webhooks", webhooks),
        new("database", "Managed Database", ComponentStatus.Operational),
    ];
}
