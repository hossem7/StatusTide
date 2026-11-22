using StatusTide.Common.Outbound;
using StatusTide.Contracts.CustomPages;
using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.Subscriber.Features.CustomPages;

/// <summary>
/// Finds a feed we can read behind a URL someone typed. Tries Statuspage JSON at the site's
/// standard path first, since it lists components, then the URL itself as RSS or Atom. Nothing
/// else is tried: no HTML is scraped. Every fetch goes through the same readers and SSRF guard as
/// polling, so what the preview shows is what polling will see.
/// </summary>
public class FeedDetector(IEnumerable<IStatusReader> readers, ILogger<FeedDetector> logger)
{
    public const string StatuspagePath = "/api/v2/summary.json";

    /// <summary>All the tries together, so a slow site can't hold a request open much longer than one fetch.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(25);

    /// <summary>Returns a <see cref="FeedDetected"/> or a <see cref="FeedNotDetected"/>.</summary>
    public async Task<object> DetectAsync(string input, CancellationToken cancellationToken)
    {
        if (!OutboundUrl.TryNormalize(input, out var url, out var error))
        {
            return new FeedNotDetected(FeedNotDetectedReason.Blocked, error);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);

        StatusReadException? last = null;
        foreach (var (candidate, format) in Candidates(url))
        {
            var reader = readers.Single(r => r.Format == format);
            try
            {
                var result = await reader.ReadAsync(candidate, null, budget.Token);
                return Detected(candidate, format, result.Snapshot!);
            }
            catch (StatusReadException e) when (e.Failure == StatusReadFailure.Blocked)
            {
                // A blocked address stays blocked for every path on it; stop here.
                return new FeedNotDetected(FeedNotDetectedReason.Blocked, e.Message);
            }
            catch (StatusReadException e)
            {
                logger.LogDebug("No {Format} feed at {Host}: {Failure}", format, candidate.Host, e.Failure);
                last = e;
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new FeedNotDetected(FeedNotDetectedReason.Unreachable, "That page took too long to answer.");
            }
        }

        // Judged by the last try, which is the URL exactly as typed.
        return last!.Failure switch
        {
            StatusReadFailure.Malformed or StatusReadFailure.TooLarge => new FeedNotDetected(
                FeedNotDetectedReason.NotAFeed,
                "We couldn't find Statuspage JSON or an RSS or Atom feed there. Try the address of the feed itself."),
            StatusReadFailure.HttpError => new FeedNotDetected(
                FeedNotDetectedReason.Unreachable,
                $"That page answered with HTTP {(int?)last.StatusCode}."),
            StatusReadFailure.Timeout => new FeedNotDetected(FeedNotDetectedReason.Unreachable, "That page took too long to answer."),
            _ => new FeedNotDetected(FeedNotDetectedReason.Unreachable, "We couldn't reach that page."),
        };
    }

    /// <summary>The URLs to try, in order, with the format to read each one as.</summary>
    public static IEnumerable<(Uri Url, FeedFormat Format)> Candidates(Uri url)
    {
        var statuspage = new Uri(url, StatuspagePath);
        yield return (statuspage, FeedFormat.Statuspage);
        if (url != statuspage)
        {
            yield return (url, FeedFormat.Rss);
        }
    }

    private static object Detected(Uri feedUrl, FeedFormat format, StatusSnapshot snapshot)
    {
        if (snapshot.Components.Count == 0)
        {
            return new FeedNotDetected(FeedNotDetectedReason.NoComponents, "That feed doesn't list anything to follow.");
        }

        return new FeedDetected(
            feedUrl.AbsoluteUri,
            new Uri(feedUrl, "/").AbsoluteUri,
            format.ToString(),
            [.. snapshot.Components.Select(c => new DetectedComponent(c.ExternalId, c.Name, c.Status.ToString()))]);
    }
}
