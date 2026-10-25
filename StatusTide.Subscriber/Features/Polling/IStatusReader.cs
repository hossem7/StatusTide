using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Reads one vendor feed into a <see cref="StatusSnapshot"/>. There is one reader per
/// feed format (Statuspage, Slack, Google Cloud, RSS or Atom, demo), picked by <see cref="Format"/>.
/// </summary>
public interface IStatusReader
{
    FeedFormat Format { get; }

    /// <summary>Fetches and parses the feed. Throws <see cref="StatusReadException"/> when it cannot.</summary>
    Task<StatusSnapshot> ReadAsync(Uri feedUrl, CancellationToken cancellationToken);
}
