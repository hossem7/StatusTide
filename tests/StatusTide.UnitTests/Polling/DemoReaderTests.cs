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
    public void Until_acme_posts_the_feed_says_all_is_well()
    {
        var snapshot = DemoReader.Snapshot(Outage, Outage.PostsAt.AddSeconds(-1));

        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
        Assert.Empty(snapshot.Incidents);
    }

    [Fact]
    public void Once_posted_the_api_is_down_and_webhooks_are_slow()
    {
        var snapshot = DemoReader.Snapshot(Outage, Outage.PostsAt.AddSeconds(5));

        Assert.Equal(ComponentStatus.MajorOutage, Status(snapshot, "api"));
        Assert.Equal(ComponentStatus.DegradedPerformance, Status(snapshot, "webhooks"));
        Assert.Equal(ComponentStatus.Operational, Status(snapshot, "database"));

        var incident = Assert.Single(snapshot.Incidents);
        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Equal(Start + DemoOutageStore.PostDelay, incident.StartedAt);
        Assert.Equal(incident.StartedAt, Assert.Single(incident.Updates).OccurredAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal(["api", "webhooks"], incident.ComponentExternalIds);
    }

    [Fact]
    public void The_incident_moves_through_identified_and_monitoring_before_it_resolves()
    {
        var identified = Assert.Single(DemoReader.Snapshot(Outage, Outage.PostsAt + DemoReader.IdentifiedAfter).Incidents);
        Assert.Equal(IncidentStatus.Identified, identified.Status);

        var snapshot = DemoReader.Snapshot(Outage, Outage.PostsAt + DemoReader.MonitoringAfter);
        var monitoring = Assert.Single(snapshot.Incidents);
        Assert.Equal(IncidentStatus.Monitoring, monitoring.Status);
        Assert.Equal(
            [IncidentStatus.Investigating, IncidentStatus.Identified, IncidentStatus.Monitoring],
            monitoring.Updates.Select(u => u.Status));
        Assert.Equal(ComponentStatus.DegradedPerformance, Status(snapshot, "api"));
        Assert.Equal(ComponentStatus.Operational, Status(snapshot, "webhooks"));

        // Every stage fits before the outage resolves itself, so a visitor who waits sees them all.
        Assert.True(Outage.PostsAt + DemoReader.MonitoringAfter < Outage.ResolvesAt);
    }

    [Fact]
    public void A_resolved_outage_stays_in_the_feed_with_its_resolve_time()
    {
        var resolvedAt = Outage.PostsAt.AddSeconds(30);
        var snapshot = DemoReader.Snapshot(Outage with { ResolvesAt = resolvedAt }, resolvedAt.AddMinutes(1));

        var incident = Assert.Single(snapshot.Incidents);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(resolvedAt, incident.ResolvedAt);
        Assert.Equal([IncidentStatus.Investigating, IncidentStatus.Resolved], incident.Updates.Select(u => u.Status));
        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
    }

    [Fact]
    public void An_outage_resolved_before_acme_posts_never_appears()
    {
        var resolved = Outage with { ResolvesAt = Start.AddMinutes(2) };

        var snapshot = DemoReader.Snapshot(resolved, Outage.PostsAt.AddMinutes(1));

        Assert.Empty(snapshot.Incidents);
        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
    }

    [Fact]
    public void Each_outage_is_a_separate_incident()
    {
        var later = new DemoOutage(Start.AddMinutes(10), Start.AddMinutes(10) + DemoOutageStore.Duration);

        var first = Assert.Single(DemoReader.Snapshot(Outage, Outage.PostsAt).Incidents);
        var second = Assert.Single(DemoReader.Snapshot(later, later.PostsAt).Incidents);

        Assert.NotEqual(first.ExternalId, second.ExternalId);
    }

    private static ComponentStatus Status(Subscriber.Features.Polling.StatusSnapshot snapshot, string externalId) =>
        snapshot.Components.Single(c => c.ExternalId == externalId).Status;
}
