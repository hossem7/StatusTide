using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.UnitTests.Polling;

public class BackoffTests
{
    static readonly Vendor EveryMinute = new()
    {
        Slug = "nimbusmail",
        Name = "NimbusMail",
        FeedUrl = "https://status.nimbusmail.example/api/v2/summary.json",
        StatusPageUrl = "https://status.nimbusmail.example",
        PollIntervalSeconds = 60,
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void Each_failure_in_a_row_doubles_the_wait(long failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), PollVendorConsumer.Backoff(EveryMinute, failures, null));
    }

    [Fact]
    public void Our_backoff_stops_growing_at_one_hour()
    {
        Assert.Equal(PollVendorConsumer.MaxBackoff, PollVendorConsumer.Backoff(EveryMinute, 50, null));
    }

    [Fact]
    public void A_longer_Retry_After_wins()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), PollVendorConsumer.Backoff(EveryMinute, 1, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void A_shorter_Retry_After_never_makes_us_poll_sooner()
    {
        Assert.Equal(TimeSpan.FromMinutes(4), PollVendorConsumer.Backoff(EveryMinute, 3, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Retry_After_is_capped_at_a_day()
    {
        Assert.Equal(PollVendorConsumer.MaxRetryAfter, PollVendorConsumer.Backoff(EveryMinute, 1, TimeSpan.FromDays(30)));
    }
}
