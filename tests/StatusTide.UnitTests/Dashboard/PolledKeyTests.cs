using StatusTide.Api.Features.Dashboard;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.UnitTests.Dashboard;

public class PolledKeyTests
{
    /// <summary>
    /// The Subscriber writes the last poll time and the API reads it. The two executables can't
    /// share code, so each defines the key; this keeps them from drifting apart.
    /// </summary>
    [Fact]
    public void The_api_reads_the_key_the_poll_worker_writes()
    {
        var vendorId = Guid.CreateVersion7();

        Assert.Equal(VendorPollState.PolledKey(vendorId), DashboardQuery.PolledKey(vendorId));
    }
}
