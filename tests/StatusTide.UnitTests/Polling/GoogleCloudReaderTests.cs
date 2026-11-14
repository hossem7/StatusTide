using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;
using StatusTide.Subscriber.Features.Polling.Google;

namespace StatusTide.UnitTests.Polling;

public class GoogleCloudReaderTests
{
    static readonly Uri FeedUrl = new("https://status.nimbus.example/incidents.json");
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-14T12:00:00Z");

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "google", name));

    /// <summary>Serves incidents.json and products.json by path, like Google's host.</summary>
    static FakeFeed Host(byte[]? incidents = null, byte[]? products = null, HttpStatusCode incidentsStatus = HttpStatusCode.OK) =>
        new((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/incidents.json" => new HttpResponseMessage(incidentsStatus) { Content = new ByteArrayContent(incidents ?? Fixture("incidents.json")) },
            "/products.json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(products ?? Fixture("products.json")) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }));

    static GoogleCloudReader Reader(FakeFeed feed)
    {
        var time = new FakeTimeProvider(Now);
        return new GoogleCloudReader(new FeedClient(new HttpClient(feed), time), time);
    }

    static async Task<StatusSnapshot> ReadAsync(FakeFeed? feed = null) =>
        (await Reader(feed ?? Host()).ReadAsync(FeedUrl, null, TestContext.Current.CancellationToken)).Snapshot!;

    [Fact]
    public void Products_are_read_from_the_same_folder_as_incidents()
    {
        Assert.Equal("https://status.nimbus.example/products.json", GoogleCloudReader.ProductsUrl(FeedUrl).AbsoluteUri);
        Assert.Equal(
            "https://status.nimbus.example/en/products.json",
            GoogleCloudReader.ProductsUrl(new Uri("https://status.nimbus.example/en/incidents.json")).AbsoluteUri);
    }

    [Fact]
    public async Task Every_product_is_a_component_keyed_by_its_stable_id()
    {
        var snapshot = await ReadAsync();

        // The current title wins over the old one; duplicates and products with no id are skipped.
        Assert.Equal(
            [("prodCompute0001", "Nimbus Compute"),
             ("prodStorage0001", "Nimbus Object Storage"),
             ("prodQueue000001", "Nimbus Queue"),
             ("prodDocsAi00001", "Nimbus Documents")],
            snapshot.Components.Select(c => (c.ExternalId, c.Name)));
    }

    [Fact]
    public async Task A_product_takes_the_worst_impact_among_its_open_incidents()
    {
        var snapshot = await ReadAsync();
        var status = snapshot.Components.ToDictionary(c => c.ExternalId, c => c.Status);

        Assert.Equal(ComponentStatus.MajorOutage, status["prodCompute0001"]);
        Assert.Equal(ComponentStatus.MajorOutage, status["prodStorage0001"]);
        Assert.Equal(ComponentStatus.DegradedPerformance, status["prodQueue000001"]);

        // Its incident ended this morning.
        Assert.Equal(ComponentStatus.Operational, status["prodDocsAi00001"]);
    }

    [Fact]
    public async Task Loads_open_incidents_and_ones_that_ended_in_the_last_day_only()
    {
        var snapshot = await ReadAsync();

        Assert.Equal(["gcpOpenOutage01", "gcpOpenInfo0001", "gcpEndedToday01"], snapshot.Incidents.Select(i => i.ExternalId));
    }

    [Fact]
    public async Task Maps_an_open_incident()
    {
        var snapshot = await ReadAsync();

        var incident = snapshot.Incidents[0];
        Assert.Equal("Compute and storage unavailable in one region", incident.Title);
        Assert.Equal(IncidentStatus.Investigating, incident.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T10:00:00Z"), incident.StartedAt);
        Assert.Null(incident.ResolvedAt);
        Assert.Equal("https://status.nimbus.example/incidents/gcpOpenOutage01", incident.SourceUrl);

        // An unknown product id is passed on; the writer drops ids it has no component for.
        Assert.Equal(["prodCompute0001", "prodStorage0001", "prodGone0000001"], incident.ComponentExternalIds);
        Assert.Equal(
            [DateTimeOffset.Parse("2026-11-14T10:05:00Z"), DateTimeOffset.Parse("2026-11-14T11:00:00Z")],
            incident.Updates.Select(u => u.OccurredAt));
    }

    [Fact]
    public async Task An_ended_incident_is_resolved_at_its_end_time()
    {
        var snapshot = await ReadAsync();

        var incident = snapshot.Incidents.Single(i => i.ExternalId == "gcpEndedToday01");
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T08:00:00Z"), incident.ResolvedAt);
        Assert.Equal([IncidentStatus.Investigating, IncidentStatus.Resolved], incident.Updates.Select(u => u.Status));
    }

    [Fact]
    public async Task A_link_to_another_host_falls_back_to_the_dashboard()
    {
        var snapshot = await ReadAsync();

        Assert.Equal("https://status.nimbus.example/", snapshot.Incidents.Single(i => i.ExternalId == "gcpOpenInfo0001").SourceUrl);
    }

    [Fact]
    public async Task Never_keeps_the_update_text()
    {
        var snapshot = await ReadAsync();

        Assert.DoesNotContain("PLACEHOLDER UPDATE TEXT", JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task An_unchanged_incident_feed_skips_the_products_request()
    {
        var feed = Host(incidentsStatus: HttpStatusCode.NotModified);

        var result = await Reader(feed).ReadAsync(FeedUrl, new FeedValidators(null, Now), TestContext.Current.CancellationToken);

        Assert.True(result.NotModified);
        Assert.Equal(1, feed.Requests);
    }

    [Theory]
    [InlineData("[]", "{}")]
    [InlineData("[]", "[]")]
    [InlineData("[1, 2]", "{\"products\": []}")]
    [InlineData("not json", "{\"products\": []}")]
    [InlineData("{}", "{\"products\": []}")]
    public async Task Rejects_feeds_that_are_not_Google_Cloud_JSON(string incidents, string products)
    {
        var feed = Host(System.Text.Encoding.UTF8.GetBytes(incidents), System.Text.Encoding.UTF8.GetBytes(products));

        var error = await Assert.ThrowsAsync<StatusReadException>(() => ReadAsync(feed));

        Assert.Equal(StatusReadFailure.Malformed, error.Failure);
    }

    [Fact]
    public async Task A_failing_products_request_fails_the_poll()
    {
        var feed = new FakeFeed((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/incidents.json"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture("incidents.json")) }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var error = await Assert.ThrowsAsync<StatusReadException>(() => ReadAsync(feed));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }
}
