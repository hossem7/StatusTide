using System.Net;

namespace StatusTide.Subscriber.Features.Polling;

public enum StatusReadFailure
{
    /// <summary>No response within <see cref="FeedClient.Timeout"/>.</summary>
    Timeout,

    /// <summary>The body was larger than <see cref="FeedClient.MaxResponseBytes"/>.</summary>
    TooLarge,

    /// <summary>The feed answered with a non-success status code.</summary>
    HttpError,

    /// <summary>DNS, connection or TLS failure.</summary>
    Unreachable,

    /// <summary>The body was not the format we expected.</summary>
    Malformed,
}

/// <summary>
/// A feed could not be read. The poll worker turns this into an "unknown" status
/// instead of retrying hard against the vendor.
/// </summary>
public class StatusReadException(StatusReadFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public StatusReadFailure Failure { get; } = failure;

    /// <summary>Set for <see cref="StatusReadFailure.HttpError"/>, so 429 and 503 can be backed off.</summary>
    public HttpStatusCode? StatusCode { get; init; }
}
