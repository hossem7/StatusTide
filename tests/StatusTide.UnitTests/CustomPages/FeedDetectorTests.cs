using System.Net;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Contracts.CustomPages;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.CustomPages;
using StatusTide.Subscriber.Features.Polling;
using StatusTide.Subscriber.Features.Polling.Rss;
using StatusTide.Subscriber.Features.Polling.Statuspage;
using StatusTide.UnitTests.Polling;

namespace StatusTide.UnitTests.CustomPages;

public class FeedDetectorTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-14T12:00:00Z");

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>A site that answers each path from <paramref name="pages"/> and 404s the rest.</summary>
    static (FeedDetector Detector, List<string> Requests) Site(Dictionary<string, byte[]> pages)
    {
        var requests = new List<string>();
        var server = new FakeFeed((request, _) =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(pages.TryGetValue(request.RequestUri.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var time = new FakeTimeProvider(Now);
        var feeds = new FeedClient(new HttpClient(server), time);
        IStatusReader[] readers =
        [
            new StatuspageReader(feeds),
            new RssReader(feeds, time, Options.Create(new RssOptions())),
        ];
        return (new FeedDetector(readers, NullLogger<FeedDetector>.Instance), requests);
    }

    static byte[] Fixture(string path) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", path));

    [Fact]
    public async Task A_statuspage_site_is_found_from_its_home_page()
    {
        var (detector, requests) = Site(new() { [FeedDetector.StatuspagePath] = Fixture("statuspage/summary-all-operational.json") });

        var detected = Assert.IsType<FeedDetected>(await detector.DetectAsync("Status.NimbusMail.example/", Cancel));

        Assert.Equal("https://status.nimbusmail.example/api/v2/summary.json", detected.FeedUrl);
        Assert.Equal("https://status.nimbusmail.example/", detected.StatusPageUrl);
        Assert.Equal(nameof(FeedFormat.Statuspage), detected.Format);
        Assert.NotEmpty(detected.Components);
        Assert.All(detected.Components, c => Assert.Equal(nameof(ComponentStatus.Operational), c.Status));
        Assert.Equal(["/api/v2/summary.json"], requests);
    }

    [Fact]
    public async Task Statuspage_json_is_tried_before_the_feed_that_was_typed()
    {
        var (detector, requests) = Site(new()
        {
            [FeedDetector.StatuspagePath] = Fixture("statuspage/summary-all-operational.json"),
            ["/history.atom"] = Fixture("rss/feed.atom"),
        });

        var detected = Assert.IsType<FeedDetected>(await detector.DetectAsync("https://status.larkspur.example/history.atom", Cancel));

        Assert.Equal(nameof(FeedFormat.Statuspage), detected.Format);
        Assert.Equal(["/api/v2/summary.json"], requests);
    }

    [Fact]
    public async Task An_atom_feed_is_found_when_the_site_has_no_statuspage_json()
    {
        var (detector, requests) = Site(new() { ["/history.atom"] = Fixture("rss/feed.atom") });

        var detected = Assert.IsType<FeedDetected>(await detector.DetectAsync("https://status.larkspur.example/history.atom#latest", Cancel));

        Assert.Equal("https://status.larkspur.example/history.atom", detected.FeedUrl);
        Assert.Equal(nameof(FeedFormat.Rss), detected.Format);
        var component = Assert.Single(detected.Components);
        Assert.Equal((RssReader.ComponentId, "Larkspur Status"), (component.ExternalId, component.Name));
        Assert.Equal(["/api/v2/summary.json", "/history.atom"], requests);
    }

    [Fact]
    public async Task A_statuspage_url_is_only_read_as_statuspage_json()
    {
        var (detector, requests) = Site(new() { [FeedDetector.StatuspagePath] = Fixture("statuspage/not-json.html") });

        var result = Assert.IsType<FeedNotDetected>(await detector.DetectAsync("https://status.nimbusmail.example/api/v2/summary.json", Cancel));

        Assert.Equal(FeedNotDetectedReason.NotAFeed, result.Reason);
        Assert.Single(requests);
    }

    [Fact]
    public async Task An_html_page_is_not_a_feed()
    {
        var (detector, _) = Site(new() { ["/"] = Fixture("statuspage/not-json.html") });

        var result = Assert.IsType<FeedNotDetected>(await detector.DetectAsync("https://status.nimbusmail.example/", Cancel));

        Assert.Equal(FeedNotDetectedReason.NotAFeed, result.Reason);
    }

    [Fact]
    public async Task A_page_that_is_not_there_is_unreachable_with_its_status_code()
    {
        var (detector, _) = Site([]);

        var result = Assert.IsType<FeedNotDetected>(await detector.DetectAsync("https://status.nimbusmail.example/feed", Cancel));

        Assert.Equal(FeedNotDetectedReason.Unreachable, result.Reason);
        Assert.Contains("404", result.Message);
    }

    [Fact]
    public async Task A_feed_with_nothing_to_follow_is_refused()
    {
        var empty = Encoding.UTF8.GetBytes("""{"components": [], "incidents": []}""");
        var (detector, _) = Site(new() { [FeedDetector.StatuspagePath] = empty });

        var result = Assert.IsType<FeedNotDetected>(await detector.DetectAsync("https://status.nimbusmail.example/", Cancel));

        Assert.Equal(FeedNotDetectedReason.NoComponents, result.Reason);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost/")]
    [InlineData("https://status.nimbusmail.example:8443/")]
    [InlineData("file:///etc/passwd")]
    public async Task A_url_that_fails_the_shape_check_is_refused_without_a_request(string url)
    {
        var (detector, requests) = Site([]);

        var result = Assert.IsType<FeedNotDetected>(await detector.DetectAsync(url, Cancel));

        Assert.Equal(FeedNotDetectedReason.Blocked, result.Reason);
        Assert.Empty(requests);
    }
}
