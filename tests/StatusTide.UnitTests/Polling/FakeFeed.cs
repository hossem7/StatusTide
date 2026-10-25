using System.Net;

namespace StatusTide.UnitTests.Polling;

/// <summary>Stands in for a vendor's server, so reader tests never touch the network.</summary>
sealed class FakeFeed(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public static readonly Uri Url = new("https://status.nimbusmail.example/api/v2/summary.json");

    public int Requests { get; private set; }

    public static FakeFeed Returning(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) }));

    public static FakeFeed ReturningFixture(string path) =>
        Returning(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", path)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return respond(request, cancellationToken);
    }
}
