namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Fetches feed bodies for every reader with the polite polling limits: a 10 second timeout
/// and a 1 MB body limit. Failures become <see cref="StatusReadException"/>.
/// </summary>
public class FeedClient(HttpClient http, TimeProvider timeProvider)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const int MaxResponseBytes = 1024 * 1024;

    public async Task<byte[]> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        // Our own timer instead of HttpClient.Timeout, so tests can move time forward.
        using var timeout = new CancellationTokenSource(Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new StatusReadException(StatusReadFailure.HttpError, $"Feed returned HTTP {(int)response.StatusCode}.")
                {
                    StatusCode = response.StatusCode,
                };
            }

            // Reject early when the server tells us the size, then count bytes anyway,
            // since Content-Length can be missing or wrong.
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw TooLarge();
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
            return await ReadLimitedAsync(body, linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new StatusReadException(StatusReadFailure.Timeout, $"Feed did not respond within {Timeout.TotalSeconds} seconds.");
        }
        catch (HttpRequestException e)
        {
            throw new StatusReadException(StatusReadFailure.Unreachable, "Feed could not be reached.", e);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static StatusReadException TooLarge() =>
        new(StatusReadFailure.TooLarge, $"Feed body is larger than {MaxResponseBytes} bytes.");
}
