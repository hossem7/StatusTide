namespace StatusTide.Contracts.CustomPages;

/// <summary>
/// Request sent by the Api when an account adds a status page by URL: find a feed we can read
/// there and say what it contains. Answered with <see cref="FeedDetected"/> or
/// <see cref="FeedNotDetected"/>. Only the Subscriber fetches outside URLs, so the Api asks.
/// </summary>
/// <param name="Url">Already normalized by the Api. The Subscriber still checks it.</param>
public record DetectFeed(string Url);

/// <summary>A readable feed was found.</summary>
/// <param name="FeedUrl">The normalized URL to poll. Pages typed differently that lead to the same feed share it.</param>
/// <param name="StatusPageUrl">Where people go to read the vendor's own page.</param>
/// <param name="Format">The feed format's name: "Statuspage" or "Rss".</param>
public record FeedDetected(
    string FeedUrl,
    string StatusPageUrl,
    string Format,
    IReadOnlyList<DetectedComponent> Components);

/// <param name="Status">The component status's name, such as "Operational".</param>
public record DetectedComponent(string ExternalId, string Name, string Status);

/// <summary>No feed we can read was found.</summary>
/// <param name="Message">Safe to show to the person who typed the URL.</param>
public record FeedNotDetected(FeedNotDetectedReason Reason, string Message);

public enum FeedNotDetectedReason
{
    /// <summary>The address, port or a redirect was refused by the SSRF guard.</summary>
    Blocked,

    /// <summary>DNS, connection, TLS failure, an error status or no answer in time.</summary>
    Unreachable,

    /// <summary>The page answered, but with neither Statuspage JSON nor RSS or Atom.</summary>
    NotAFeed,

    /// <summary>A feed was found but lists nothing to follow.</summary>
    NoComponents,
}
