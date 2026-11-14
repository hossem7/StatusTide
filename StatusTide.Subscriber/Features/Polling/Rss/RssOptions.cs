using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling.Rss;

/// <summary>
/// Title rules for RSS and Atom feeds, from "Polling:Rss". A feed item has no status field, only
/// a title, so each vendor's wording is written down during verification. Feeds without their
/// own rules use <see cref="DefaultRules"/>.
/// </summary>
public class RssOptions
{
    public List<RssFeedRules> Feeds { get; init; } = [];

    /// <summary>
    /// Used after a feed's own rules. Common "resolved" prefixes close an incident; any other
    /// recent item counts as degraded, since the feed does not say how bad it is.
    /// </summary>
    public static readonly IReadOnlyList<RssTitleRule> DefaultRules =
    [
        new() { TitleStartsWith = "[RESOLVED]", Resolves = true },
        new() { TitleStartsWith = "RESOLVED", Resolves = true },
        new() { TitleStartsWith = "[COMPLETED]", Resolves = true },
        new() { TitleStartsWith = "", Status = ComponentStatus.DegradedPerformance },
    ];
}

/// <summary>Rules for one feed, matched by the feed URL's host.</summary>
public class RssFeedRules
{
    public required string Host { get; init; }

    /// <summary>How long an item stays open without a resolve. Older items leave the snapshot.</summary>
    public TimeSpan OpenFor { get; init; } = RssReader.DefaultOpenFor;

    /// <summary>Tried in order; the first match wins.</summary>
    public List<RssTitleRule> Rules { get; init; } = [];
}

/// <summary>
/// Matches an item title, ignoring case. Set <see cref="TitleStartsWith"/> or
/// <see cref="TitleContains"/>. A match either resolves (the item is resolved and closes the
/// items before it) or sets the component to <see cref="Status"/>.
/// </summary>
public class RssTitleRule
{
    public string? TitleStartsWith { get; init; }
    public string? TitleContains { get; init; }
    public ComponentStatus Status { get; init; } = ComponentStatus.DegradedPerformance;
    public bool Resolves { get; init; }

    public bool Matches(string title) =>
        (TitleStartsWith is { } prefix && title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || (TitleContains is { Length: > 0 } part && title.Contains(part, StringComparison.OrdinalIgnoreCase));
}
