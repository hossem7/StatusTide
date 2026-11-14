using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;
using StatusTide.Subscriber.Features.Polling.Rss;

namespace StatusTide.UnitTests.Polling;

public class RssReaderTests
{
    static readonly Uri AwsStyleUrl = new("https://health.nimbus.example/rss/all.rss");
    static readonly Uri AtomUrl = new("https://status.larkspur.example/feed.atom");
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-14T12:00:00Z");

    /// <summary>Rules in the shape a verified vendor gets in "Polling:Rss". The wording is this fixture's.</summary>
    static readonly RssOptions NimbusRules = new()
    {
        Feeds =
        [
            new RssFeedRules
            {
                Host = "health.nimbus.example",
                Rules =
                [
                    new RssTitleRule { TitleStartsWith = "Service is operating normally", Resolves = true },
                    new RssTitleRule { TitleStartsWith = "Informational message", Status = ComponentStatus.Operational },
                    new RssTitleRule { TitleStartsWith = "Service degradation", Status = ComponentStatus.DegradedPerformance },
                    new RssTitleRule { TitleStartsWith = "Service disruption", Status = ComponentStatus.PartialOutage },
                ],
            },
        ],
    };

    static async Task<StatusSnapshot> ReadAsync(Uri url, string fixture, RssOptions? options = null, byte[]? body = null)
    {
        var time = new FakeTimeProvider(Now);
        var feed = body is null ? FakeFeed.ReturningFixture($"rss/{fixture}") : FakeFeed.Returning(body);
        var reader = new RssReader(new FeedClient(new HttpClient(feed), time), time, Options.Create(options ?? new RssOptions()));
        return (await reader.ReadAsync(url, null, TestContext.Current.CancellationToken)).Snapshot!;
    }

    static Task<StatusSnapshot> ReadXmlAsync(string xml) =>
        ReadAsync(AtomUrl, "", body: Encoding.UTF8.GetBytes(xml));

    [Fact]
    public async Task The_whole_feed_is_one_component_named_after_the_feed()
    {
        var snapshot = await ReadAsync(AtomUrl, "feed.atom");

        var component = Assert.Single(snapshot.Components);
        Assert.Equal((RssReader.ComponentId, "Larkspur Status"), (component.ExternalId, component.Name));
    }

    [Fact]
    public async Task A_resolving_item_closes_the_items_before_it()
    {
        var snapshot = await ReadAsync(AwsStyleUrl, "aws-style.rss", NimbusRules);

        var disruption = snapshot.Incidents.Single(i => i.Title.StartsWith("Service disruption"));
        Assert.Equal(IncidentStatus.Resolved, disruption.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T09:00:00Z"), disruption.StartedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T10:30:00Z"), disruption.ResolvedAt);
        Assert.Equal([IncidentStatus.Investigating, IncidentStatus.Resolved], disruption.Updates.Select(u => u.Status));

        var resolve = snapshot.Incidents.Single(i => i.Title.StartsWith("Service is operating normally"));
        Assert.Equal(IncidentStatus.Resolved, resolve.Status);
    }

    [Fact]
    public async Task The_vendors_rules_set_the_level_of_items_still_open()
    {
        var snapshot = await ReadAsync(AwsStyleUrl, "aws-style.rss", NimbusRules);

        // Only the informational item is still open, and this vendor's rules call that operational.
        var open = Assert.Single(snapshot.Incidents, i => i.ResolvedAt is null);
        Assert.Equal("Informational message: Console sign-in slow", open.Title);
        Assert.Equal(ComponentStatus.Operational, snapshot.Components[0].Status);
    }

    [Fact]
    public async Task Without_its_own_rules_a_feed_uses_the_default_rules()
    {
        var snapshot = await ReadAsync(AwsStyleUrl, "aws-style.rss");

        // Nothing starts with a known "resolved" word, so all three recent items are open and degraded.
        Assert.Equal(3, snapshot.Incidents.Count(i => i.ResolvedAt is null));
        Assert.Equal(ComponentStatus.DegradedPerformance, snapshot.Components[0].Status);
    }

    [Fact]
    public async Task Skips_items_older_than_the_open_window_and_items_without_a_title_or_date()
    {
        var snapshot = await ReadAsync(AwsStyleUrl, "aws-style.rss", NimbusRules);

        Assert.Equal(3, snapshot.Incidents.Count);
        Assert.DoesNotContain(snapshot.Incidents, i => i.ExternalId is "old-item-1" or "no-title" or "no-date");
    }

    [Fact]
    public async Task Uses_guids_as_ids_and_replaces_unsafe_links()
    {
        var snapshot = await ReadAsync(AwsStyleUrl, "aws-style.rss", NimbusRules);

        var console = snapshot.Incidents.Single(i => i.Title.StartsWith("Informational"));
        Assert.Equal("https://health.nimbus.example/#console-1763118900", console.ExternalId);
        Assert.Equal("https://health.nimbus.example/", console.SourceUrl);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T11:15:00Z"), console.StartedAt);
    }

    [Fact]
    public async Task Reads_Atom_entries_with_their_alternate_link_and_both_dates()
    {
        var snapshot = await ReadAsync(AtomUrl, "feed.atom");

        var resolved = snapshot.Incidents.Single(i => i.ExternalId.EndsWith("incident-7"));
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T08:00:00Z"), resolved.StartedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T09:45:00Z"), resolved.ResolvedAt);
        Assert.Equal("https://status.larkspur.example/incidents/7", resolved.SourceUrl);

        var open = snapshot.Incidents.Single(i => i.ExternalId.EndsWith("incident-8"));
        Assert.Null(open.ResolvedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T10:00:00Z"), open.StartedAt);
        Assert.Equal("https://status.larkspur.example/incidents/8", open.SourceUrl);
        Assert.Equal(ComponentStatus.DegradedPerformance, snapshot.Components[0].Status);
    }

    [Fact]
    public async Task A_quiet_feed_is_operational()
    {
        var snapshot = await ReadXmlAsync("""<feed xmlns="http://www.w3.org/2005/Atom"><title>Quiet</title></feed>""");

        Assert.Equal(ComponentStatus.Operational, Assert.Single(snapshot.Components).Status);
        Assert.Empty(snapshot.Incidents);
    }

    [Fact]
    public async Task A_long_id_is_hashed_instead_of_cut()
    {
        var longId = "https://status.larkspur.example/" + new string('a', 300);
        var snapshot = await ReadXmlAsync($"""
            <rss version="2.0"><channel><title>T</title>
              <item><title>Something broke</title><guid>{longId}</guid><pubDate>Sat, 14 Nov 2026 11:00:00 GMT</pubDate></item>
            </channel></rss>
            """);

        var id = Assert.Single(snapshot.Incidents).ExternalId;
        Assert.StartsWith("sha256:", id);
        Assert.True(id.Length <= FeedText.MaxIdLength);
    }

    [Fact]
    public async Task Never_keeps_descriptions_or_content()
    {
        var everything = JsonSerializer.Serialize(await ReadAsync(AwsStyleUrl, "aws-style.rss", NimbusRules))
            + JsonSerializer.Serialize(await ReadAsync(AtomUrl, "feed.atom"));

        Assert.DoesNotContain("PLACEHOLDER", everything);
    }

    [Theory]
    [InlineData("external-entity.xml")]
    [InlineData("not-a-feed.xml")]
    [InlineData("../statuspage/summary-all-operational.json")]
    public async Task Rejects_DTDs_and_anything_that_is_not_RSS_or_Atom(string fixture)
    {
        var error = await Assert.ThrowsAsync<StatusReadException>(() => ReadAsync(AtomUrl, fixture));

        Assert.Equal(StatusReadFailure.Malformed, error.Failure);
    }

    [Theory]
    [InlineData("Sat, 14 Nov 2026 11:00:00 GMT", "2026-11-14T11:00:00Z")]
    [InlineData("Sat, 14 Nov 2026 03:00:00 PST", "2026-11-14T11:00:00Z")]
    [InlineData("Sat, 14 Nov 2026 06:00:00 -0500", "2026-11-14T11:00:00Z")]
    [InlineData("14 Nov 2026 11:00:00 +0000", "2026-11-14T11:00:00Z")]
    [InlineData("Mon, 14 Nov 2026 11:00:00 GMT", "2026-11-14T11:00:00Z")]
    [InlineData("2026-11-14T12:00:00+01:00", "2026-11-14T11:00:00Z")]
    [InlineData("2026-11-14T11:00:00Z", "2026-11-14T11:00:00Z")]
    public void Parses_RSS_and_Atom_dates(string value, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), RssReader.ParseDate(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData(null)]
    public void An_unreadable_date_is_null(string? value)
    {
        Assert.Null(RssReader.ParseDate(value));
    }
}
