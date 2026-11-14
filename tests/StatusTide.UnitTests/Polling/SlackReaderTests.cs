using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;
using StatusTide.Subscriber.Features.Polling.Slack;

namespace StatusTide.UnitTests.Polling;

public class SlackReaderTests
{
    static readonly Uri FeedUrl = new("https://slack-status.example/api/v2.0.0/current");

    static async Task<StatusSnapshot> ReadFixtureAsync(string name)
    {
        var reader = new SlackReader(new FeedClient(new HttpClient(FakeFeed.ReturningFixture($"slack/{name}")), new FakeTimeProvider()));
        var result = await reader.ReadAsync(FeedUrl, null, TestContext.Current.CancellationToken);
        return result.Snapshot!;
    }

    [Fact]
    public async Task An_ok_feed_lists_every_documented_service_as_operational()
    {
        var snapshot = await ReadFixtureAsync("current-ok.json");

        Assert.Equal(SlackReader.Services, snapshot.Components.Select(c => c.ExternalId));
        Assert.Equal(SlackReader.Services, snapshot.Components.Select(c => c.Name));
        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
        Assert.Empty(snapshot.Incidents);
    }

    [Fact]
    public async Task A_service_takes_the_worst_level_among_open_incidents_that_name_it()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");
        var status = snapshot.Components.ToDictionary(c => c.ExternalId, c => c.Status);

        // Messaging is hit by an outage and an incident; the outage wins.
        Assert.Equal(ComponentStatus.MajorOutage, status["Messaging"]);
        Assert.Equal(ComponentStatus.MajorOutage, status["Files"]);

        // A resolved incident and a planned maintenance leave their services alone.
        Assert.Equal(ComponentStatus.Operational, status["Search"]);
        Assert.Equal(ComponentStatus.Operational, status["Huddles"]);

        // An incident with no id is skipped, so it does not mark its service either.
        Assert.Equal(ComponentStatus.Operational, status["Login/SSO"]);
    }

    [Fact]
    public async Task A_service_Slack_has_not_documented_is_added_after_the_known_ones()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");

        var last = snapshot.Components[^1];
        Assert.Equal(("Calls", ComponentStatus.PartialOutage), (last.ExternalId, last.Status));
        Assert.Equal(SlackReader.Services.Count + 1, snapshot.Components.Count);
    }

    [Fact]
    public async Task Maps_an_active_incident_with_note_times_oldest_first()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");

        var incident = snapshot.Incidents.Single(i => i.ExternalId == "9001");
        Assert.Equal("Messages failing to send for some users", incident.Title);
        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T10:10:00Z"), incident.StartedAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal("https://slack-status.example/2026-11/aaaa0001", incident.SourceUrl);
        Assert.Equal(["Messaging", "Files"], incident.ComponentExternalIds);
        Assert.Equal(
            [DateTimeOffset.Parse("2026-11-14T10:15:00Z"), DateTimeOffset.Parse("2026-11-14T11:40:00Z")],
            incident.Updates.Select(u => u.OccurredAt));
    }

    [Fact]
    public async Task A_resolved_incident_ends_at_its_last_update()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");

        var incident = snapshot.Incidents.Single(i => i.ExternalId == "9003");
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T09:30:00Z"), incident.ResolvedAt);
        Assert.Equal(IncidentStatus.Resolved, incident.Updates[^1].Status);
    }

    [Fact]
    public async Task Accepts_a_numeric_id_trims_titles_and_replaces_unsafe_links()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");

        var incident = snapshot.Incidents.Single(i => i.ExternalId == "9002");
        Assert.Equal("Slow message delivery", incident.Title);
        Assert.Equal("https://slack-status.example/", incident.SourceUrl);
    }

    [Fact]
    public async Task Skips_planned_maintenance_and_incidents_without_an_id()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");

        Assert.Equal(["9001", "9002", "9003"], snapshot.Incidents.Select(i => i.ExternalId));
    }

    [Fact]
    public async Task Never_keeps_the_note_text()
    {
        var snapshot = await ReadFixtureAsync("current-active.json");

        Assert.DoesNotContain("PLACEHOLDER UPDATE TEXT", JsonSerializer.Serialize(snapshot));
    }

    [Theory]
    [InlineData("not-slack.json")]
    [InlineData("../statuspage/not-json.html")]
    public async Task Rejects_a_body_that_is_not_Slack_status_JSON(string name)
    {
        var error = await Assert.ThrowsAsync<StatusReadException>(() => ReadFixtureAsync(name));

        Assert.Equal(StatusReadFailure.Malformed, error.Failure);
    }
}
