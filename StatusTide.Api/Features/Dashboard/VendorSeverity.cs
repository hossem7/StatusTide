using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Dashboard;

/// <summary>
/// How bad each component status is, for the "worst status wins" rule that gives a vendor its
/// status. The order is a product decision, pinned in a test, and kept here in code because
/// neither the enum's order nor its text in PostgreSQL is a severity order.
/// </summary>
public static class VendorSeverity
{
    /// <summary>
    /// Higher is worse. "Unknown" (we cannot read the feed) ranks above planned maintenance,
    /// since not knowing is more worrying than work the vendor announced.
    /// </summary>
    public static int Rank(ComponentStatus status) => status switch
    {
        ComponentStatus.Operational => 0,
        ComponentStatus.UnderMaintenance => 1,
        ComponentStatus.Unknown => 2,
        ComponentStatus.DegradedPerformance => 3,
        ComponentStatus.PartialOutage => 4,
        ComponentStatus.MajorOutage => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No severity for this status."),
    };

    /// <summary>The worst of the given statuses, or Unknown when there are none.</summary>
    public static ComponentStatus Worst(IEnumerable<ComponentStatus> statuses) =>
        statuses.DefaultIfEmpty(ComponentStatus.Unknown).MaxBy(Rank);
}
