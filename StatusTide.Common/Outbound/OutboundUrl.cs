using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace StatusTide.Common.Outbound;

/// <summary>
/// Checks the shape of a URL the server will fetch, and writes it one way so the same page
/// typed differently is stored (and polled) once. The address check happens again at connect
/// time in <see cref="OutboundHandler"/>, since DNS can answer differently later.
/// </summary>
public static class OutboundUrl
{
    public const int MaxLength = 2048;

    /// <summary>
    /// Parses <paramref name="input"/> as an absolute http or https URL on the default port, with
    /// no user name or password. A missing scheme means https. The result has a lower-case host
    /// (punycode for international names), no default port and no fragment.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out Uri? url, [NotNullWhen(false)] out string? error)
    {
        url = null;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "Enter a URL.";
            return false;
        }

        if (text.Length > MaxLength)
        {
            error = "That URL is too long.";
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.HostNameType == UriHostNameType.Unknown)
        {
            error = "That isn't a valid URL.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = "Only http and https URLs are supported.";
            return false;
        }

        if (!parsed.IsDefaultPort)
        {
            error = "Only the standard ports (80 for http, 443 for https) are supported.";
            return false;
        }

        if (parsed.UserInfo.Length > 0)
        {
            error = "URLs with a user name or password aren't supported.";
            return false;
        }

        // An address typed as numbers is judged now; a name is resolved when we connect.
        if (IPAddress.TryParse(parsed.DnsSafeHost, out var literal) && BlockedAddresses.IsBlocked(literal))
        {
            error = "That address is on a private or reserved network.";
            return false;
        }

        if (parsed.HostNameType == UriHostNameType.Dns && !parsed.IdnHost.Contains('.', StringComparison.Ordinal))
        {
            error = "Use the page's full public address.";
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Host = parsed.HostNameType == UriHostNameType.Dns ? parsed.IdnHost.ToLowerInvariant().TrimEnd('.') : parsed.Host,
            Port = -1,
            Fragment = "",
        };

        url = builder.Uri;
        if (url.AbsoluteUri.Length > MaxLength)
        {
            url = null;
            error = "That URL is too long.";
            return false;
        }

        error = null;
        return true;
    }
}
