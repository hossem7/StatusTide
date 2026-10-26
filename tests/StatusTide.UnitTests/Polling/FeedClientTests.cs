using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Microsoft.Extensions.Configuration;
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

        var result = await Client(FakeFeed.Returning(body)).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken);

        Assert.Equal(FeedClient.MaxResponseBytes, result.Body!.Length);
    }

    [Fact]
    public async Task A_body_over_1_MB_is_rejected_while_reading()
    {
        // StreamContent sends no Content-Length, so only counting the bytes can catch it.
        var body = new MemoryStream(Padded("{}", FeedClient.MaxResponseBytes + 1));
        var feed = new FakeFeed((_, _) => Task.FromResult(new HttpResponseMessage { Content = new StreamContent(body) }));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.TooLarge, error.Failure);
    }

    [Fact]
    public async Task A_large_Content_Length_is_rejected_before_reading_the_body()
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 50L * 1024 * 1024;
        var feed = new FakeFeed((_, _) => Task.FromResult(new HttpResponseMessage { Content = content }));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

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

        var read = Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken);
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

        var read = Client(feed).GetAsync(FakeFeed.Url, null, shutdown.Token);
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
            () => Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.HttpError, error.Failure);
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task A_connection_failure_is_reported_as_unreachable()
    {
        var feed = new FakeFeed((_, _) => throw new HttpRequestException("Connection refused"));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusReadFailure.Unreachable, error.Failure);
    }

    [Fact]
    public async Task Validators_from_the_last_read_are_sent_as_conditional_headers()
    {
        var lastModified = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
        HttpRequestMessage? sent = null;
        var feed = new FakeFeed((request, _) =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });

        await Client(feed).GetAsync(FakeFeed.Url, new FeedValidators("\"v42\"", lastModified), TestContext.Current.CancellationToken);

        Assert.Equal("\"v42\"", Assert.Single(sent!.Headers.IfNoneMatch).Tag);
        Assert.Equal(lastModified, sent.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Not_modified_returns_no_body_and_keeps_the_validators()
    {
        var validators = new FeedValidators("\"v42\"", null);
        var feed = new FakeFeed((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)));

        var response = await Client(feed).GetAsync(FakeFeed.Url, validators, TestContext.Current.CancellationToken);

        Assert.True(response.NotModified);
        Assert.Equal(validators, response.Validators);
    }

    [Fact]
    public async Task ETag_and_Last_Modified_are_returned_for_the_next_read()
    {
        var lastModified = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
        var feed = new FakeFeed((_, _) =>
        {
            var response = new HttpResponseMessage { Content = new ByteArrayContent("{}"u8.ToArray()) };
            response.Headers.ETag = new EntityTagHeaderValue("\"v43\"");
            response.Content.Headers.LastModified = lastModified;
            return Task.FromResult(response);
        });

        var result = await Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken);

        Assert.Equal(new FeedValidators("\"v43\"", lastModified), result.Validators);
    }

    [Fact]
    public async Task Retry_After_in_seconds_is_reported()
    {
        var feed = Failing(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(120), error.RetryAfter);
    }

    [Fact]
    public async Task Retry_After_as_a_date_is_reported_as_the_wait_until_then()
    {
        var feed = Failing(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(time.GetUtcNow().AddMinutes(5)));

        var error = await Assert.ThrowsAsync<StatusReadException>(
            () => Client(feed).GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken));

        // HTTP dates have whole seconds.
        Assert.NotNull(error.RetryAfter);
        Assert.InRange(error.RetryAfter.Value, TimeSpan.FromSeconds(299), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Requests_name_the_StatusTide_bot()
    {
        string? userAgent = null;
        var feed = new FakeFeed((request, _) =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent("{}"u8.ToArray()) });
        });
        var services = new ServiceCollection().AddPolling(new ConfigurationBuilder().Build());
        services.AddHttpClient<FeedClient>().ConfigurePrimaryHttpMessageHandler(() => feed);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        await provider.GetRequiredService<FeedClient>().GetAsync(FakeFeed.Url, null, TestContext.Current.CancellationToken);

        Assert.Equal(PollingExtensions.DefaultUserAgent, userAgent);
    }

    [Fact]
    public void AddPolling_registers_a_reader_for_Statuspage()
    {
        using var provider = new ServiceCollection().AddPolling(new ConfigurationBuilder().Build()).BuildServiceProvider(validateScopes: true);

        var readers = provider.GetServices<IStatusReader>();

        Assert.Contains(readers, r => r.Format == FeedFormat.Statuspage);
    }

    static FakeFeed Failing(HttpStatusCode status, RetryConditionHeaderValue retryAfter) =>
        new((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.RetryAfter = retryAfter;
            return Task.FromResult(response);
        });

    /// <summary>Valid JSON padded with trailing spaces to an exact size.</summary>
    static byte[] Padded(string json, int size) =>
        Encoding.UTF8.GetBytes(json.PadRight(size));
}
