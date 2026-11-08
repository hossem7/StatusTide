using StatusTide.Data.Vendors;

namespace StatusTide.Data.Checks;

/// <summary>
/// A stretch of time when our own checks found a vendor down: opened after
/// <see cref="CheckPolicy.FailuresToMarkDown"/> failures in a row, closed by the next success.
/// Kept long term, after the raw checks are gone, because the incident page compares
/// <see cref="DetectedAt"/> with the vendor's first post.
/// </summary>
public class CheckOutage
{
    public long Id { get; init; }
    public Guid VendorId { get; set; }

    /// <summary>When the first failed check of the streak ran.</summary>
    public DateTimeOffset FirstFailedAt { get; set; }

    /// <summary>When the failure that crossed the threshold ran: the moment we call the vendor down.</summary>
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>When the first successful check after the outage ran. Null while it lasts.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public Vendor? Vendor { get; set; }
}
