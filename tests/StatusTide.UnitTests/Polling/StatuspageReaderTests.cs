using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;
using StatusTide.Subscriber.Features.Polling.Statuspage;

namespace StatusTide.UnitTests.Polling;

public class StatuspageReaderTests
{
    static StatuspageReader Reader(FakeFeed feed) => new(new FeedClient(new HttpClient(feed), new FakeTimeProvider()));

    static async Task<StatusSnapshot> ReadFixtureAsync(string name)
    {
        var result = await Reader(FakeFeed.ReturningFixture($"statuspage/{name}")).ReadAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken);
        return result.Snapshot!;
    }

    [Fact]
    public async Task Maps_components_and_skips_groups()
    {
        var snapshot = await ReadFixtureAsync("summary-all-operational.json");

        Assert.Equal(["Send API", "Inbox API", "Dashboard"], snapshot.Components.Select(c => c.Name));
        Assert.All(snapshot.Components, c => Assert.Equal(ComponentStatus.Operational, c.Status));
        Assert.Empty(snapshot.Incidents);
    }

    [Fact]
    public async Task Maps_every_Statuspage_component_status()
    {
        var snapshot = await ReadFixtureAsync("summary-active-incident.json");

        Assert.Equal(
            [ComponentStatus.MajorOutage, ComponentStatus.PartialOutage, ComponentStatus.DegradedPerformance, ComponentStatus.UnderMaintenance],
            snapshot.Components.Select(c => c.Status));
    }

    [Fact]
    public async Task Maps_an_open_incident_with_its_timeline_and_affected_components()
    {
        var snapshot = await ReadFixtureAsync("summary-active-incident.json");

        var incident = Assert.Single(snapshot.Incidents);
        Assert.Equal("nmbinc000001", incident.ExternalId);
        Assert.Equal("Emails delayed or failing to send", incident.Title);
        Assert.Equal(IncidentStatus.Identified, incident.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T10:01:00Z"), incident.StartedAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal("https://stspg.example/nmb1", incident.SourceUrl);
        Assert.Equal(["nmbcompsend1", "nmbcompinbx1"], incident.ComponentExternalIds);

        // Updates come newest first in the feed; the timeline is oldest first.
        Assert.Equal([IncidentStatus.Investigating, IncidentStatus.Identified], incident.Updates.Select(u => u.Status));
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T10:05:00Z"), incident.Updates[0].OccurredAt);
    }

    [Fact]
    public async Task Never_keeps_the_vendor_update_text()
    {
        var snapshot = await ReadFixtureAsync("summary-active-incident.json");

        var everything = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("PLACEHOLDER UPDATE TEXT", everything);
    }

    [Fact]
    public async Task Skips_or_cleans_bad_components_instead_of_failing()
    {
        var snapshot = await ReadFixtureAsync("summary-hostile.json");

        // Duplicate id kept once, missing id or name skipped, names trimmed,
        // unknown or missing statuses become Unknown.
        Assert.Equal(
            [("brkcompdns01", "Authoritative DNS", ComponentStatus.Unknown),
             ("brkcompcdn01", "CDN", ComponentStatus.Unknown),
             ("brkcomprsv01", "Resolver", ComponentStatus.Operational)],
            snapshot.Components.Select(c => (c.ExternalId, c.Name, c.Status)));
    }

    [Fact]
    public async Task Skips_or_cleans_bad_incidents_instead_of_failing()
    {
        var snapshot = await ReadFixtureAsync("summary-hostile.json");

        // The incidents without an id or without any time are dropped.
        Assert.Equal(["brkinc000001", "brkinc000004"], snapshot.Incidents.Select(i => i.ExternalId));

        var open = snapshot.Incidents[0];
        Assert.Equal(new string('A', 500), open.Title);
        Assert.Equal(IncidentStatus.Investigating, open.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T10:50:00Z"), open.StartedAt);
        Assert.Equal("https://status.nimbusmail.example/incidents/brkinc000001", open.SourceUrl);
        Assert.Equal(["brkcompdns01", "brkcompcdn01"], open.ComponentExternalIds);
        Assert.Single(open.Updates);

        var resolved = snapshot.Incidents[1];
        Assert.Equal("Untitled incident", resolved.Title);
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T09:30:00Z"), resolved.ResolvedAt);
    }

    [Theory]
    [InlineData("not-json.html")]
    [InlineData("truncated.json")]
    public async Task A_body_that_is_not_Statuspage_JSON_is_malformed(string fixture)
    {
        var error = await Assert.ThrowsAsync<StatusReadException>(() => ReadFixtureAsync(fixture));

        Assert.Equal(StatusReadFailure.Malformed, error.Failure);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"components\": \"not a list\"}")]
    public async Task Valid_JSON_without_a_components_list_is_malformed(string json)
    {
        var feed = FakeFeed.Returning(Encoding.UTF8.GetBytes(json));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Reader(feed).ReadAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.Malformed, error.Failure);
    }

    [Fact]
    public async Task An_oversized_feed_is_rejected()
    {
        var feed = FakeFeed.Returning(new byte[FeedClient.MaxResponseBytes + 1]);

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Reader(feed).ReadAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.TooLarge, error.Failure);
    }
}
