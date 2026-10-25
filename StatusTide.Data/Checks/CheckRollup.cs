using StatusTide.Data.Vendors;

namespace StatusTide.Data.Checks;

/// <summary>One hour of a vendor's checks, kept long term for uptime charts.</summary>
public class CheckRollup
{
    public Guid VendorId { get; set; }

    /// <summary>Start of the hour, in UTC.</summary>
    public DateTimeOffset HourStart { get; set; }

    public int TotalChecks { get; set; }
    public int SuccessfulChecks { get; set; }
    public decimal UptimePercent { get; set; }
    public int? AverageResponseTimeMs { get; set; }

    public Vendor? Vendor { get; set; }
}
