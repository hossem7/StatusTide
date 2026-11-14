using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using Microsoft.Extensions.Options;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling.Rss;

/// <summary>
/// Reads any RSS 2.0 or Atom feed, for vendors that publish nothing else. A feed has no
/// components, so the whole feed is one component. Each recent item is an incident, its title
/// decides how serious it is (see <see cref="RssOptions"/>), and the newest item tells the
/// current state. Only titles, times and links are read; descriptions and content never are.
/// </summary>
public class RssReader(FeedClient feeds, TimeProvider timeProvider, IOptions<RssOptions> options) : IStatusReader
{
    /// <summary>The single component's id. Kept fixed so follows survive a feed renaming itself.</summary>
    public const string ComponentId = "feed";

    public static readonly TimeSpan DefaultOpenFor = TimeSpan.FromHours(24);

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    // No DTDs and no external entities: a hostile feed must not make us fetch files or expand bombs.
    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public FeedFormat Format => FeedFormat.Rss;

    public async Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken)
    {
        var response = await feeds.GetAsync(feedUrl, validators, cancellationToken);
        if (response.Body is not { } body)
        {
            return new StatusReadResult(null, response.Validators);
        }

        var (feedTitle, items) = Parse(body);
        var rules = options.Value.Feeds.FirstOrDefault(f => string.Equals(f.Host, feedUrl.Host, StringComparison.OrdinalIgnoreCase));
        var openFor = rules?.OpenFor ?? DefaultOpenFor;
        var now = timeProvider.GetUtcNow();

        var incidents = MapIncidents(
            items.Where(i => now - i.UpdatedAt <= openFor),
            [.. (rules?.Rules ?? []), .. RssOptions.DefaultRules],
            feedUrl,
            out var status);

        var component = new ComponentSnapshot(ComponentId, feedTitle ?? feedUrl.Host, status);
        return new StatusReadResult(new StatusSnapshot([component], incidents), response.Validators);
    }

    private static List<IncidentSnapshot> MapIncidents(IEnumerable<FeedItem> items, List<RssTitleRule> rules, Uri feedUrl, out ComponentStatus status)
    {
        var incidents = new List<IncidentSnapshot>();
        var open = new List<(int Index, ComponentStatus Level)>();

        // Oldest first, so a resolving item can close the ones posted before it.
        foreach (var item in items.DistinctBy(i => i.Id).OrderBy(i => i.UpdatedAt))
        {
            var rule = rules.First(r => r.Matches(item.Title));
            if (rule.Resolves)
            {
                foreach (var (index, _) in open)
                {
                    incidents[index] = Resolve(incidents[index], item.UpdatedAt);
                }

                open.Clear();
                incidents.Add(Resolve(Open(item, feedUrl), item.UpdatedAt));
                continue;
            }

            open.Add((incidents.Count, rule.Status));
            incidents.Add(Open(item, feedUrl));
        }

        status = open.Select(o => o.Level).Aggregate(ComponentStatus.Operational, FeedText.Worse);
        return incidents;
    }

    private static IncidentSnapshot Open(FeedItem item, Uri feedUrl) => new(
        item.Id,
        item.Title,
        IncidentStatus.Investigating,
        item.PublishedAt,
        null,
        FeedText.HttpUrl(item.Link) ?? new Uri(feedUrl, "/").AbsoluteUri,
        [ComponentId],
        [new IncidentUpdateSnapshot(IncidentStatus.Investigating, item.PublishedAt)]);

    private static IncidentSnapshot Resolve(IncidentSnapshot incident, DateTimeOffset at)
    {
        var resolvedAt = at < incident.StartedAt ? incident.StartedAt : at;
        return incident with
        {
            Status = IncidentStatus.Resolved,
            ResolvedAt = resolvedAt,
            Updates = [.. incident.Updates.Where(u => u.OccurredAt < resolvedAt), new IncidentUpdateSnapshot(IncidentStatus.Resolved, resolvedAt)],
        };
    }

    private static (string? Title, List<FeedItem> Items) Parse(byte[] body)
    {
        XElement root;
        try
        {
            using var stream = new MemoryStream(body);
            using var reader = XmlReader.Create(stream, XmlSettings);
            root = XDocument.Load(reader).Root
                ?? throw new StatusReadException(StatusReadFailure.Malformed, "Feed has no root element.");
        }
        catch (XmlException e)
        {
            throw new StatusReadException(StatusReadFailure.Malformed, "Feed is not valid XML.", e);
        }

        if (root.Name == Atom + "feed")
        {
            return (FeedText.Clean(root.Element(Atom + "title")?.Value, FeedText.MaxNameLength), root.Elements(Atom + "entry").Select(AtomItem).OfType<FeedItem>().ToList());
        }

        if (root.Name == "rss" && root.Element("channel") is { } channel)
        {
            return (FeedText.Clean(channel.Element("title")?.Value, FeedText.MaxNameLength), channel.Elements("item").Select(RssItem).OfType<FeedItem>().ToList());
        }

        throw new StatusReadException(StatusReadFailure.Malformed, "Feed is neither RSS 2.0 nor Atom.");
    }

    private static FeedItem? AtomItem(XElement entry)
    {
        var updated = ParseDate(entry.Element(Atom + "updated")?.Value);
        var published = ParseDate(entry.Element(Atom + "published")?.Value) ?? updated;
        var link = entry.Elements(Atom + "link")
            .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")?
            .Attribute("href")?.Value;

        return Item(entry.Element(Atom + "id")?.Value, entry.Element(Atom + "title")?.Value, link, published, updated ?? published);
    }

    private static FeedItem? RssItem(XElement item)
    {
        var date = ParseDate(item.Element("pubDate")?.Value);
        return Item(item.Element("guid")?.Value, item.Element("title")?.Value, item.Element("link")?.Value, date, date);
    }

    private static FeedItem? Item(string? rawId, string? rawTitle, string? link, DateTimeOffset? publishedAt, DateTimeOffset? updatedAt)
    {
        var title = FeedText.Clean(rawTitle, FeedText.MaxTitleLength);
        if (title is null || publishedAt is null || updatedAt is null)
        {
            return null;
        }

        var id = FeedText.Clean(rawId, int.MaxValue) ?? FeedText.Clean(link, int.MaxValue) ?? $"{title}|{publishedAt:O}";

        // An id longer than the column would be cut, and two cut ids could collide, so long ones are hashed.
        if (id.Length > FeedText.MaxIdLength)
        {
            id = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        }

        return new FeedItem(id, title, link, publishedAt.Value, updatedAt.Value < publishedAt.Value ? publishedAt.Value : updatedAt.Value);
    }

    /// <summary>Atom's RFC 3339 dates, and RSS's RFC 822 dates with either an offset or a zone name.</summary>
    public static DateTimeOffset? ParseDate(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var space = text.LastIndexOf(' ');
        if (space > 0 && Zones.TryGetValue(text[(space + 1)..], out var offset))
        {
            text = text[..space] + " " + offset;
        }
        else if (space > 0 && text[(space + 1)..] is { Length: 5 } numeric && (numeric[0] is '+' or '-') && numeric[1..].All(char.IsAsciiDigit))
        {
            text = text[..space] + " " + numeric[..3] + ":" + numeric[3..];
        }

        // RSS puts the weekday first; it adds nothing and a wrong one would fail the parse.
        var comma = text.IndexOf(',');
        if (comma is > 0 and <= 4)
        {
            text = text[(comma + 1)..].TrimStart();
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    // The zone names RFC 822 allows.
    private static readonly Dictionary<string, string> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = "+00:00", ["UT"] = "+00:00", ["UTC"] = "+00:00", ["Z"] = "+00:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
    };

    private sealed record FeedItem(string Id, string Title, string? Link, DateTimeOffset PublishedAt, DateTimeOffset UpdatedAt);
}
