using StatusTide.Common.Demo;
using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling.Demo;

namespace StatusTide.UnitTests.Polling;

public class DemoReaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DemoOutage Outage = new(Start, Start + DemoOutageStore.Duration);

    [Fact]
    public void Without_an_outage_everything_is_operational_with_no_incidents()
    {
        var snapshot = DemoReader.Snapshot(null, Start);

        Assert.Equal(["api", "dashboard", "webhooks", "database"], snapshot.Components.Select(c => c.ExternalId));
        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
        Assert.Empty(snapshot.Incidents);
    }

    [Fact]
    public void A_new_outage_takes_the_api_down_and_slows_webhooks()
    {
        var snapshot = DemoReader.Snapshot(Outage, Start.AddSeconds(5));

        Assert.Equal(ComponentStatus.MajorOutage, Status(snapshot, "api"));
        Assert.Equal(ComponentStatus.DegradedPerformance, Status(snapshot, "webhooks"));
        Assert.Equal(ComponentStatus.Operational, Status(snapshot, "database"));

        var incident = Assert.Single(snapshot.Incidents);
        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Equal(Start, incident.StartedAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal(["api", "webhooks"], incident.ComponentExternalIds);
    }

    [Fact]
    public void The_incident_moves_through_identified_and_monitoring()
    {
        var identified = Assert.Single(DemoReader.Snapshot(Outage, Start + DemoReader.IdentifiedAfter).Incidents);
        Assert.Equal(IncidentStatus.Identified, identified.Status);

        var snapshot = DemoReader.Snapshot(Outage, Start + DemoReader.MonitoringAfter);
        var monitoring = Assert.Single(snapshot.Incidents);
        Assert.Equal(IncidentStatus.Monitoring, monitoring.Status);
        Assert.Equal(
            [IncidentStatus.Investigating, IncidentStatus.Identified, IncidentStatus.Monitoring],
            monitoring.Updates.Select(u => u.Status));
        Assert.Equal(ComponentStatus.DegradedPerformance, Status(snapshot, "api"));
        Assert.Equal(ComponentStatus.Operational, Status(snapshot, "webhooks"));
    }

    [Fact]
    public void A_resolved_outage_stays_in_the_feed_with_its_resolve_time()
    {
        var resolvedAt = Start.AddSeconds(30);
        var snapshot = DemoReader.Snapshot(Outage with { ResolvesAt = resolvedAt }, resolvedAt.AddMinutes(1));

        var incident = Assert.Single(snapshot.Incidents);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(resolvedAt, incident.ResolvedAt);
        Assert.Equal([IncidentStatus.Investigating, IncidentStatus.Resolved], incident.Updates.Select(u => u.Status));
        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
    }

    [Fact]
    public void Each_outage_is_a_separate_incident()
    {
        var later = new DemoOutage(Start.AddMinutes(10), Start.AddMinutes(15));

        var first = Assert.Single(DemoReader.Snapshot(Outage, Start).Incidents);
        var second = Assert.Single(DemoReader.Snapshot(later, later.StartedAt).Incidents);

        Assert.NotEqual(first.ExternalId, second.ExternalId);
    }

    private static ComponentStatus Status(Subscriber.Features.Polling.StatusSnapshot snapshot, string externalId) =>
        snapshot.Components.Single(c => c.ExternalId == externalId).Status;
}
