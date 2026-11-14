using System.Text.Json;

using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling.Google;

/// <summary>
/// Reads Google Cloud Service Health. The feed URL is incidents.json, the incident history.
/// Components are the products in products.json on the same host, so every product can be
/// followed before its first incident. Products and incidents are matched by product id, which
/// Google documents as stable; display names may change without warning.
/// </summary>
public class GoogleCloudReader(FeedClient feeds, TimeProvider timeProvider) : IStatusReader
{
    /// <summary>
    /// How long an ended incident stays in the snapshot. Long enough that its real end time is
    /// saved even after a few missed polls; older history is never loaded.
    /// </summary>
    public static readonly TimeSpan RecentlyEnded = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new StringOrNumberConverter() },
    };

    public FeedFormat Format => FeedFormat.Google;

    public async Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken)
    {
        // Validators belong to incidents.json. When it has not changed, products.json is not fetched either.
        var response = await feeds.GetAsync(feedUrl, validators, cancellationToken);
        if (response.Body is not { } body)
        {
            return new StatusReadResult(null, response.Validators);
        }

        var incidents = Parse<List<GoogleIncident>>(body, "incidents");
        var productsResponse = await feeds.GetAsync(ProductsUrl(feedUrl), null, cancellationToken);
        var productsBody = productsResponse.Body
            ?? throw new StatusReadException(StatusReadFailure.Malformed, "Products feed answered 304 to a plain request.");
        var products = Parse<GoogleProducts>(productsBody, "products").Products
            ?? throw new StatusReadException(StatusReadFailure.Malformed, "Products feed has no products list.");

        var now = timeProvider.GetUtcNow();
        var mapped = incidents
            .Where(i => i.End is null || now - i.End <= RecentlyEnded)
            .Select(i => MapIncident(i, feedUrl))
            .OfType<(IncidentSnapshot Incident, ComponentStatus Impact)>()
            .ToList();

        return new StatusReadResult(new StatusSnapshot(MapComponents(products, mapped), mapped.Select(m => m.Incident).ToList()), response.Validators);
    }

    /// <summary>products.json lives next to incidents.json.</summary>
    public static Uri ProductsUrl(Uri feedUrl) => new(feedUrl, "products.json");

    private static T Parse<T>(byte[] body, string what) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new StatusReadException(StatusReadFailure.Malformed, $"Google Cloud {what} feed is empty.");
        }
        catch (JsonException e)
        {
            throw new StatusReadException(StatusReadFailure.Malformed, $"Google Cloud {what} feed is not valid JSON.", e);
        }
    }

    private static List<ComponentSnapshot> MapComponents(List<GoogleProduct> products, List<(IncidentSnapshot Incident, ComponentStatus Impact)> incidents)
    {
        var impact = new Dictionary<string, ComponentStatus>();
        foreach (var (incident, level) in incidents.Where(i => i.Incident.ResolvedAt is null))
        {
            foreach (var id in incident.ComponentExternalIds)
            {
                impact[id] = FeedText.Worse(impact.GetValueOrDefault(id, ComponentStatus.Operational), level);
            }
        }

        var mapped = new List<ComponentSnapshot>();
        var seen = new HashSet<string>();
        foreach (var product in products)
        {
            var id = FeedText.Clean(product.Id, FeedText.MaxIdLength);
            var name = FeedText.Clean(product.CurrentTitle, FeedText.MaxNameLength) ?? FeedText.Clean(product.Title, FeedText.MaxNameLength);
            if (id is null || name is null || !seen.Add(id))
            {
                continue;
            }

            mapped.Add(new ComponentSnapshot(id, name, impact.GetValueOrDefault(id, ComponentStatus.Operational)));
        }

        return mapped;
    }

    private static (IncidentSnapshot, ComponentStatus)? MapIncident(GoogleIncident incident, Uri feedUrl)
    {
        var id = FeedText.Clean(incident.Id, FeedText.MaxIdLength);
        var startedAt = incident.Begin ?? incident.Created;
        if (id is null || startedAt is null)
        {
            return null;
        }

        // The update text is never read. Google's update statuses are impact levels rather than
        // investigation stages, so every update counts as investigating, and the last one at or
        // after the end counts as the resolve.
        var updates = (incident.Updates ?? [])
            .Select(u => u.When ?? u.Created)
            .OfType<DateTimeOffset>()
            .Distinct()
            .Order()
            .Select(when => new IncidentUpdateSnapshot(incident.End is { } end && when >= end ? IncidentStatus.Resolved : IncidentStatus.Investigating, when))
            .ToList();

        // Google lists the products affected over the whole incident, with no per-product end.
        var products = (incident.AffectedProducts ?? [])
            .Select(p => FeedText.Clean(p.Id, FeedText.MaxIdLength))
            .OfType<string>()
            .Distinct()
            .ToList();

        var snapshot = new IncidentSnapshot(
            id,
            FeedText.Clean(incident.ExternalDesc, FeedText.MaxTitleLength) ?? "Untitled incident",
            incident.End is null ? IncidentStatus.Investigating : IncidentStatus.Resolved,
            startedAt.Value,
            incident.End,
            SourceUrl(incident.Uri, feedUrl),
            products,
            updates);

        return (snapshot, Impact(incident.StatusImpact, incident.MostRecentUpdate?.Status));
    }

    /// <summary>Google's uri is a path such as "incidents/abc" on the dashboard; full links are kept as they are.</summary>
    private static string SourceUrl(string? uri, Uri feedUrl)
    {
        if (FeedText.HttpUrl(uri) is { } absolute)
        {
            return absolute;
        }

        if (Uri.TryCreate(uri?.Trim(), UriKind.Relative, out var relative)
            && new Uri(new Uri(feedUrl, "/"), relative) is { } joined
            && joined.Host == feedUrl.Host
            && FeedText.HttpUrl(joined.AbsoluteUri) is { } link)
        {
            return link;
        }

        return new Uri(feedUrl, "/").AbsoluteUri;
    }

    /// <summary>
    /// Google's schema gives no list of values here. These are the ones shown on the dashboard;
    /// anything else still marks the product as affected.
    /// </summary>
    private static ComponentStatus Impact(string? statusImpact, string? latestStatus) =>
        (latestStatus ?? statusImpact)?.ToUpperInvariant() switch
        {
            "SERVICE_OUTAGE" => ComponentStatus.MajorOutage,
            "SERVICE_DISRUPTION" => ComponentStatus.PartialOutage,
            _ => ComponentStatus.DegradedPerformance,
        };

    private sealed record GoogleProducts(List<GoogleProduct>? Products);

    private sealed record GoogleProduct(string? Id, string? Title, string? CurrentTitle);

    private sealed record GoogleIncident(
        string? Id,
        DateTimeOffset? Begin,
        DateTimeOffset? Created,
        DateTimeOffset? End,
        string? ExternalDesc,
        string? StatusImpact,
        string? Uri,
        List<GoogleUpdate>? Updates,
        GoogleUpdate? MostRecentUpdate,
        List<GoogleProductRef>? AffectedProducts);

    // The update "text" is never read.
    private sealed record GoogleUpdate(DateTimeOffset? When, DateTimeOffset? Created, string? Status);

    private sealed record GoogleProductRef(string? Id);
}
