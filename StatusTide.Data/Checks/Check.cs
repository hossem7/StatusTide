using StatusTide.Data.Vendors;

namespace StatusTide.Data.Checks;

/// <summary>
/// One result from our own checks of a vendor's health URL. Raw rows are kept for 7 days;
/// after that only the hourly <see cref="CheckRollup"/> remains.
/// </summary>
public class Check
{
    public long Id { get; init; }
    public Guid VendorId { get; set; }

    public required string TargetUrl { get; set; }
    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>Null when the request never got a response, such as a timeout or DNS failure.</summary>
    public int? StatusCode { get; set; }

    public int? ResponseTimeMs { get; set; }
    public bool Succeeded { get; set; }

    public Vendor? Vendor { get; set; }
}
