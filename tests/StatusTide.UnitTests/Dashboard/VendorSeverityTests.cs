using StatusTide.Api.Features.Dashboard;
using StatusTide.Data.Vendors;

namespace StatusTide.UnitTests.Dashboard;

public class VendorSeverityTests
{
    /// <summary>
    /// The agreed product order, from worst to best. Changing it changes what every dashboard,
    /// alert and incident page says, so it is pinned here on purpose.
    /// </summary>
    [Fact]
    public void Statuses_rank_from_major_outage_down_to_operational()
    {
        ComponentStatus[] worstFirst =
        [
            ComponentStatus.MajorOutage,
            ComponentStatus.PartialOutage,
            ComponentStatus.DegradedPerformance,
            ComponentStatus.Unknown,
            ComponentStatus.UnderMaintenance,
            ComponentStatus.Operational,
        ];

        Assert.Equal(worstFirst, Enum.GetValues<ComponentStatus>().OrderByDescending(VendorSeverity.Rank));
    }

    [Theory]
    [InlineData(new[] { ComponentStatus.Operational, ComponentStatus.Operational }, ComponentStatus.Operational)]
    [InlineData(new[] { ComponentStatus.Operational, ComponentStatus.Unknown }, ComponentStatus.Unknown)]
    [InlineData(new[] { ComponentStatus.Unknown, ComponentStatus.UnderMaintenance }, ComponentStatus.Unknown)]
    [InlineData(new[] { ComponentStatus.Unknown, ComponentStatus.DegradedPerformance }, ComponentStatus.DegradedPerformance)]
    [InlineData(new[] { ComponentStatus.PartialOutage, ComponentStatus.MajorOutage, ComponentStatus.Operational }, ComponentStatus.MajorOutage)]
    public void The_worst_status_wins(ComponentStatus[] statuses, ComponentStatus expected) =>
        Assert.Equal(expected, VendorSeverity.Worst(statuses));

    [Fact]
    public void No_statuses_is_unknown() =>
        Assert.Equal(ComponentStatus.Unknown, VendorSeverity.Worst([]));
}
