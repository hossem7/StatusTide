using MassTransit;

using StatusTide.Common.Outbound;
using StatusTide.Contracts.CustomPages;

namespace StatusTide.Api.Features.CustomPages;

/// <summary>
/// Asks the Subscriber to look for a feed at a URL. The Api never fetches the URL itself: the URL
/// is only checked for shape here, so obvious mistakes come back without a round trip.
/// </summary>
public class FeedDetection(IRequestClient<DetectFeed> client)
{
    /// <summary>A little longer than the Subscriber's own budget, so its answer arrives first.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <returns>A <see cref="FeedDetected"/>, a <see cref="FeedNotDetected"/>, or null when the Subscriber didn't answer in time.</returns>
    public async Task<object?> DetectAsync(string? input, CancellationToken cancellationToken)
    {
        if (!OutboundUrl.TryNormalize(input, out var url, out var error))
        {
            return new FeedNotDetected(FeedNotDetectedReason.Blocked, error);
        }

        try
        {
            var response = await client.GetResponse<FeedDetected, FeedNotDetected>(
                new DetectFeed(url.AbsoluteUri),
                cancellationToken,
                RequestTimeout.After(s: (int)Timeout.TotalSeconds));
            return response.Is(out Response<FeedDetected>? detected) ? detected.Message
                : response.Is(out Response<FeedNotDetected>? notDetected) ? notDetected.Message
                : null;
        }
        catch (RequestTimeoutException)
        {
            return null;
        }
    }
}
