using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Data.Vendors;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.UnitTests.Polling;

public class FeedClientTests
{
    readonly FakeTimeProvider time = new();

    FeedClient Client(FakeFeed feed) => new(new HttpClient(feed), time);

    [Fact]
    public async Task A_body_of_exactly_1_MB_is_accepted()
    {
        var body = Padded("{}", FeedClient.MaxResponseBytes);

        var result = await Client(FakeFeed.Returning(body)).GetAsync(FakeFeed.Url, TestContext.Current.CancellationToken);

        Assert.Equal(FeedClient.MaxResponseBytes, result.Length);
    }

    [Fact]
    public async Task A_body_over_1_MB_is_rejected_while_reading()
    {
        // StreamContent sends no Content-Length, so only counting the bytes can catch it.
        var body = new MemoryStream(Padded("{}", FeedClient.MaxResponseBytes + 1));
        var feed = new FakeFeed((_, _) => Task.FromResult(new HttpResponseMessage { Content = new StreamContent(body) }));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.TooLarge, error.Failure);
    }

    [Fact]
    public async Task A_large_Content_Length_is_rejected_before_reading_the_body()
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 50L * 1024 * 1024;
        var feed = new FakeFeed((_, _) => Task.FromResult(new HttpResponseMessage { Content = content }));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.TooLarge, error.Failure);
    }

    [Fact]
    public async Task A_feed_that_does_not_answer_in_10_seconds_times_out()
    {
        var feed = new FakeFeed(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var read = Client(feed).GetAsync(FakeFeed.Url, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(9.9));
        Assert.False(read.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(0.1));

        var error = await Assert.ThrowsAsync<StatusReadException>(() => read);
        Assert.Equal(StatusReadFailure.Timeout, error.Failure);
    }

    [Fact]
    public async Task Shutting_down_cancels_without_reporting_a_feed_failure()
    {
        using var shutdown = new CancellationTokenSource();
        var feed = new FakeFeed(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var read = Client(feed).GetAsync(FakeFeed.Url, shutdown.Token);
        await shutdown.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task An_error_status_is_reported_with_its_code(HttpStatusCode status)
    {
        var feed = FakeFeed.Returning(Encoding.UTF8.GetBytes("{}"), status);

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.HttpError, error.Failure);
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task A_connection_failure_is_reported_as_unreachable()
    {
        var feed = new FakeFeed((_, _) => throw new HttpRequestException("Connection refused"));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.Unreachable, error.Failure);
    }

    [Fact]
    public void AddPolling_registers_a_reader_for_Statuspage()
    {
        using var provider = new ServiceCollection().AddPolling().BuildServiceProvider(validateScopes: true);

        var readers = provider.GetServices<IStatusReader>();

        Assert.Contains(readers, r => r.Format == FeedFormat.Statuspage);
    }

    /// <summary>Valid JSON padded with trailing spaces to an exact size.</summary>
    static byte[] Padded(string json, int size) =>
        Encoding.UTF8.GetBytes(json.PadRight(size));
}
