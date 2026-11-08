using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Common.Demo;
using StatusTide.Subscriber.Features.Checks;
using StatusTide.UnitTests.Polling;

namespace StatusTide.UnitTests.Checks;

public class HealthProbeTests
{
    static readonly Uri Url = new("https://api.nimbusmail.example/health");

    readonly FakeTimeProvider time = new();

    // The demo store is only used for demo URLs, which the integration tests cover with real Redis.
    HealthProbe Probe(FakeFeed server) => new(new HttpClient(server), new DemoOutageStore(null!, time), time);

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Found, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task Only_a_2xx_answer_counts_as_up(HttpStatusCode status, bool up)
    {
        var result = await Probe(FakeFeed.Returning([], status)).ProbeAsync(Url, Cancel);

        Assert.Equal((int)status, result.StatusCode);
        Assert.Equal(up, result.Succeeded);
    }

    [Fact]
    public async Task Records_the_time_until_the_answer_arrives()
    {
        var server = new FakeFeed((_, _) =>
        {
            time.Advance(TimeSpan.FromMilliseconds(237));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var result = await Probe(server).ProbeAsync(Url, Cancel);

        Assert.Equal(237, result.ResponseTimeMs);
    }

    [Fact]
    public async Task No_answer_in_10_seconds_is_a_failure_with_no_status_code()
    {
        var server = new FakeFeed(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var probe = Probe(server).ProbeAsync(Url, Cancel);
        time.Advance(TimeSpan.FromSeconds(9.9));
        Assert.False(probe.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(0.1));

        Assert.Equal(new ProbeResult(null, null, false), await probe);
    }

    [Fact]
    public async Task An_unreachable_server_is_a_failure_with_no_status_code()
    {
        var server = new FakeFeed((_, _) => throw new HttpRequestException("No such host"));

        Assert.Equal(new ProbeResult(null, null, false), await Probe(server).ProbeAsync(Url, Cancel));
    }

    [Fact]
    public async Task Shutting_down_cancels_instead_of_recording_a_failure()
    {
        using var shutdown = new CancellationTokenSource();
        var server = new FakeFeed(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var probe = Probe(server).ProbeAsync(Url, shutdown.Token);
        await shutdown.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
    }

    [Theory]
    [InlineData("https://api.nimbusmail.example/health", true)]
    [InlineData("http://api.nimbusmail.example/health", true)]
    [InlineData("demo://acme-cloud/health", true)]
    [InlineData("https://api.nimbusmail.example:8443/health", false)]
    [InlineData("http://api.nimbusmail.example:8080/health", false)]
    [InlineData("ftp://api.nimbusmail.example/health", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Only_http_and_https_on_default_ports_or_the_demo_url_are_supported(string url, bool supported) =>
        Assert.Equal(supported, HealthProbe.IsSupported(new Uri(url)));

    /// <summary>
    /// Against a real local server, through the client the Subscriber registers: a redirect is
    /// recorded as it is and never followed, and the request names our bot.
    /// </summary>
    [Fact]
    public async Task The_registered_probe_does_not_follow_redirects_and_sends_our_user_agent()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var requested = new List<string>();
        string? userAgent = null;
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            requested.Add(context.Request.Url!.AbsolutePath);
            userAgent = context.Request.UserAgent;
            context.Response.StatusCode = 302;
            context.Response.RedirectLocation = $"http://localhost:{port}/elsewhere";
            context.Response.Close();
        }, Cancel);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Polling:UserAgent"] = "StatusTideTest/1.0" })
            .Build();
        await using var services = new ServiceCollection()
            .AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ => null!)
            .AddChecks(configuration)
            .BuildServiceProvider();

        var result = await services.GetRequiredService<HealthProbe>().ProbeAsync(new Uri($"http://localhost:{port}/health"), Cancel);
        await server;

        Assert.Equal(302, result.StatusCode);
        Assert.False(result.Succeeded);
        Assert.Equal(["/health"], requested);
        Assert.Equal("StatusTideTest/1.0", userAgent);
    }

    static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
