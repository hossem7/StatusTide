using System.Net;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Fetches feed bodies for every reader with the polite polling rules: conditional requests,
/// a 10 second timeout and a 1 MB body limit. Failures become <see cref="StatusReadException"/>.
/// </summary>
public class FeedClient(HttpClient http, TimeProvider timeProvider)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const int MaxResponseBytes = 1024 * 1024;

    /// <summary>Fetches the feed. Pass the validators from the last response to get a cheap 304 when nothing changed.</summary>
    public async Task<FeedResponse> GetAsync(Uri url, FeedValidators? validators, CancellationToken cancellationToken)
    {
        // Our own timer instead of HttpClient.Timeout, so tests can move time forward.
        using var timeout = new CancellationTokenSource(Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (validators?.ETag is { } etag)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (validators?.LastModified is { } lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // A 304 may leave out the validators; the ones we sent are still current.
                return new FeedResponse(null, validators ?? new FeedValidators(null, null));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new StatusReadException(StatusReadFailure.HttpError, $"Feed returned HTTP {(int)response.StatusCode}.")
                {
                    StatusCode = response.StatusCode,
                    RetryAfter = RetryAfter(response),
                };
            }

            // Reject early when the server tells us the size, then count bytes anyway,
            // since Content-Length can be missing or wrong.
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw TooLarge();
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
            var bytes = await ReadLimitedAsync(body, linked.Token);
            return new FeedResponse(bytes, new FeedValidators(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified));
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

    /// <summary>Retry-After as a wait, from either its seconds form or its date form.</summary>
    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - timeProvider.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
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
