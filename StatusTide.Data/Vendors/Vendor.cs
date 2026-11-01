namespace StatusTide.Data.Vendors;

/// <summary>
/// A status page we poll: a verified catalog vendor or a custom page added by an account.
/// One row per feed URL, so a page added by several users is polled once.
/// </summary>
public class Vendor
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>URL-friendly name used in links, for example "acme-cloud".</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>The catalog filter this vendor appears under. Null for custom pages.</summary>
    public VendorCategory? Category { get; set; }

    /// <summary>The machine-readable source we poll (Statuspage JSON, RSS or Atom).</summary>
    public required string FeedUrl { get; set; }

    /// <summary>The human status page, where "read the full update" links go.</summary>
    public required string StatusPageUrl { get; set; }

    public FeedFormat Format { get; set; }

    /// <summary>Public, unauthenticated health URL for our own checks, chosen during verification.</summary>
    public string? CheckUrl { get; set; }

    /// <summary>True for verified vendors anyone can follow; false for custom pages.</summary>
    public bool IsCatalog { get; set; }

    /// <summary>False stops polling, for example when a vendor asks to be removed.</summary>
    public bool IsEnabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; }

    /// <summary>When the scheduler should poll next. Null means as soon as possible.</summary>
    public DateTimeOffset? NextPollAt { get; set; }

    public DateTimeOffset? LastPolledAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }

    public List<Component> Components { get; } = [];
    public List<VendorVerification> Verifications { get; } = [];
}
