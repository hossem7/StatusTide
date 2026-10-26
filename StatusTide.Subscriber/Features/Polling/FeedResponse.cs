using System.Text.Json.Serialization;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// What a feed told us about its last response, sent back on the next poll as If-None-Match
/// and If-Modified-Since. The feed then answers 304 when nothing changed.
/// </summary>
public record FeedValidators(string? ETag, DateTimeOffset? LastModified)
{
    [JsonIgnore]
    public bool IsEmpty => ETag is null && LastModified is null;
}

/// <summary>A feed body, or no body when the feed answered 304 Not Modified.</summary>
public record FeedResponse(byte[]? Body, FeedValidators Validators)
{
    public bool NotModified => Body is null;
}
