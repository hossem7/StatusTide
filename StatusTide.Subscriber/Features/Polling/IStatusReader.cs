using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Reads one vendor feed into a <see cref="StatusSnapshot"/>. There is one reader per
/// feed format (Statuspage, Slack, Google Cloud, RSS or Atom, demo), picked by <see cref="Format"/>.
/// </summary>
public interface IStatusReader
{
    FeedFormat Format { get; }

    /// <summary>
    /// Fetches and parses the feed, sending <paramref name="validators"/> from the last read so an
    /// unchanged feed can answer 304. Throws <see cref="StatusReadException"/> when it cannot read it.
    /// </summary>
    Task<StatusReadResult> ReadAsync(Uri feedUrl, FeedValidators? validators, CancellationToken cancellationToken);
}

/// <summary>The snapshot, or none when the feed said nothing changed, plus the validators for the next read.</summary>
public record StatusReadResult(StatusSnapshot? Snapshot, FeedValidators Validators)
{
    public bool NotModified => Snapshot is null;
}
