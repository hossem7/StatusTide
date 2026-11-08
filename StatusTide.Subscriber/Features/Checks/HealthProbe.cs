using StatusTide.Common.Demo;

namespace StatusTide.Subscriber.Features.Checks;

/// <summary>What one check found. A null status code means no response at all.</summary>
public record ProbeResult(int? StatusCode, int? ResponseTimeMs, bool Succeeded);

/// <summary>
/// Calls a vendor's health URL once. Only a 2xx answer within <see cref="Timeout"/> counts as up.
/// Redirects are not followed: a health URL is chosen to answer directly, and following one
/// would send us somewhere nobody checked. The demo URL fetches nothing and fails while a
/// simulated outage runs.
/// </summary>
public class HealthProbe(HttpClient http, DemoOutageStore demo, TimeProvider timeProvider)
{
    public const string DemoScheme = "demo";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Plain http or https on the default ports, or the demo URL. Check URLs are chosen by us
    /// during vendor verification, so anything else is a mistake in the vendor row.
    /// </summary>
    public static bool IsSupported(Uri url) =>
        url.Scheme == DemoScheme
        || (url.Scheme == Uri.UriSchemeHttps && url.Port == 443)
        || (url.Scheme == Uri.UriSchemeHttp && url.Port == 80);

    public async Task<ProbeResult> ProbeAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme == DemoScheme)
        {
            var outage = await demo.GetAsync();
            return outage is not null && outage.IsActiveAt(timeProvider.GetUtcNow())
                ? new ProbeResult(503, null, false)
                : new ProbeResult(200, null, true);
        }

        // Our own timer instead of HttpClient.Timeout, so tests can move time forward.
        using var timeout = new CancellationTokenSource(Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var started = timeProvider.GetTimestamp();
        try
        {
            // Headers only: the body is never read or stored.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var elapsed = (int)Math.Round(timeProvider.GetElapsedTime(started).TotalMilliseconds);
            var code = (int)response.StatusCode;
            return new ProbeResult(code, elapsed, code is >= 200 and < 300);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(null, null, false);
        }
        catch (HttpRequestException)
        {
            // DNS failure, refused connection, TLS error: the vendor is unreachable from here.
            return new ProbeResult(null, null, false);
        }
    }
}
