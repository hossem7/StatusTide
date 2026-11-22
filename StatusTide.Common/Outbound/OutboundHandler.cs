using System.Net;
using System.Net.Sockets;

namespace StatusTide.Common.Outbound;

public class OutboundOptions
{
    /// <summary>
    /// Host names allowed to skip the address and port checks, such as a local fixture server
    /// while testing by hand. Always empty in production.
    /// </summary>
    public string[] TrustedHosts { get; set; } = [];
}

/// <summary>The guard refused to connect. The message is safe to show to the person who typed the URL.</summary>
public class OutboundBlockedException(string message) : Exception(message);

/// <summary>
/// The HTTP handler for every request the server makes to a URL it did not write itself (the SSRF
/// guard). Each connection, including one opened for a redirect, resolves the host here, refuses
/// ports other than 80 and 443 and any blocked address, and then connects to the exact address it
/// checked. Checking and connecting in one place means DNS can't answer one way for the check and
/// another way for the connection.
/// </summary>
public static class OutboundHandler
{
    public const int MaxRedirects = 5;

    /// <param name="followRedirects">
    /// Follow up to <see cref="MaxRedirects"/> redirects. .NET never follows https to http, and
    /// every hop opens its connection through the same check.
    /// </param>
    public static SocketsHttpHandler Create(OutboundOptions options, bool followRedirects)
    {
        var trusted = options.TrustedHosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            MaxAutomaticRedirections = MaxRedirects,

            // A proxy would make the check see the proxy's address instead of the real target.
            UseProxy = false,

            // DNS answers change; don't keep a connection to an old address forever.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, trusted, cancellationToken),
        };
    }

    /// <summary>The addresses a connection to <paramref name="host"/> may use, or an exception saying why there are none.</summary>
    public static async Task<IPAddress[]> ResolveAsync(string host, int port, IReadOnlySet<string> trustedHosts, CancellationToken cancellationToken)
    {
        var trusted = trustedHosts.Contains(host);
        if (!trusted && port is not (80 or 443))
        {
            throw new OutboundBlockedException("Only the standard ports (80 and 443) are allowed.");
        }

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        // Refuse when any answer is blocked, not just the first: a name that points at both a
        // public and a private address is not a normal public site.
        if (!trusted && addresses.Any(BlockedAddresses.IsBlocked))
        {
            throw new OutboundBlockedException("That address is on a private or reserved network.");
        }

        return addresses;
    }

    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, IReadOnlySet<string> trusted, CancellationToken cancellationToken)
    {
        var addresses = await ResolveAsync(endpoint.Host, endpoint.Port, trusted, cancellationToken);

        // Dual mode, so the same socket can reach IPv4 and IPv6 answers.
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>True when <paramref name="error"/> or one of its inner exceptions came from the guard.</summary>
    public static bool IsBlocked(Exception error, out string message)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is OutboundBlockedException blocked)
            {
                message = blocked.Message;
                return true;
            }
        }

        message = "";
        return false;
    }
}
