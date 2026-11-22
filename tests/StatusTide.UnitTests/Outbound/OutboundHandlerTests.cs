using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.Time.Testing;

using StatusTide.Common.Outbound;
using StatusTide.Subscriber.Features.Polling;

namespace StatusTide.UnitTests.Outbound;

/// <summary>
/// The guard against real sockets. Nothing here leaves the machine: blocked requests are refused
/// before connecting, and the one server is a listener on loopback that the test trusts by name.
/// </summary>
public class OutboundHandlerTests
{
    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    static HttpClient Client(bool followRedirects, params string[] trustedHosts) =>
        new(OutboundHandler.Create(new OutboundOptions { TrustedHosts = trustedHosts }, followRedirects));

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://localhost/")] // a name that resolves to loopback
    [InlineData("https://status.nimbusmail.example:8443/")] // port refused before any DNS lookup
    public async Task Blocked_targets_are_refused_before_connecting(string url)
    {
        using var client = Client(followRedirects: true);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url, Cancel));

        Assert.True(OutboundHandler.IsBlocked(error, out var message));
        Assert.NotEmpty(message);
    }

    [Theory]
    [InlineData("http://127.0.0.1/secret")] // blocked address on a standard port
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://status.nimbusmail.example:8080/")] // public-looking name on another port
    public async Task A_redirect_to_a_blocked_target_is_refused(string location)
    {
        await using var server = await RedirectServer.StartAsync(_ => location);
        using var client = Client(followRedirects: true, "localhost");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://localhost:{server.Port}/feed", Cancel));

        Assert.True(OutboundHandler.IsBlocked(error, out _));
        Assert.Equal(["/feed"], server.Paths);
    }

    [Fact]
    public async Task A_trusted_host_skips_the_checks()
    {
        await using var server = await RedirectServer.StartAsync(_ => null);
        using var client = Client(followRedirects: true, "localhost");

        using var response = await client.GetAsync($"http://localhost:{server.Port}/feed", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_feed_client_reports_a_blocked_address_as_blocked()
    {
        var feeds = new FeedClient(Client(followRedirects: true), new FakeTimeProvider());

        var error = await Assert.ThrowsAsync<StatusReadException>(() =>
            feeds.GetAsync(new Uri("http://169.254.169.254/latest/meta-data/"), null, Cancel));

        Assert.Equal(StatusReadFailure.Blocked, error.Failure);
    }

    /// <summary>
    /// A tiny HTTP server on loopback. It answers each request with a redirect to the location
    /// the test gives, or a 200 when that is null, then closes the connection.
    /// </summary>
    sealed class RedirectServer : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly Func<int, string?> _location;
        readonly CancellationTokenSource _stop = new();
        readonly Task _loop;

        RedirectServer(Func<int, string?> location)
        {
            _location = location;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<string> Paths { get; } = [];

        public static Task<RedirectServer> StartAsync(Func<int, string?> location) => Task.FromResult(new RedirectServer(location));

        async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                using (tcp)
                {
                    var stream = tcp.GetStream();
                    var buffer = new byte[4096];
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    var requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
                    Paths.Add(requestLine.Split(' ')[1]);

                    var location = _location(Port);
                    var response = location is null
                        ? "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : $"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            _stop.Dispose();
        }
    }
}
