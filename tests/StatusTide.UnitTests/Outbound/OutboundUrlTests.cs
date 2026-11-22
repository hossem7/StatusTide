using StatusTide.Common.Outbound;

namespace StatusTide.UnitTests.Outbound;

public class OutboundUrlTests
{
    [Theory]
    [InlineData("https://status.nimbusmail.example/", "https://status.nimbusmail.example/")]
    [InlineData("status.nimbusmail.example", "https://status.nimbusmail.example/")]
    [InlineData("  HTTPS://Status.NimbusMail.Example:443/History.atom#top  ", "https://status.nimbusmail.example/History.atom")]
    [InlineData("http://status.nimbusmail.example:80/feed?x=1", "http://status.nimbusmail.example/feed?x=1")]
    [InlineData("https://status.nimbusmail.example./", "https://status.nimbusmail.example/")]
    [InlineData("https://bücher.example/feed", "https://xn--bcher-kva.example/feed")]
    [InlineData("https://8.8.8.8/feed", "https://8.8.8.8/feed")]
    public void Writes_the_same_page_one_way(string input, string expected)
    {
        Assert.True(OutboundUrl.TryNormalize(input, out var url, out var error), error);
        Assert.Equal(expected, url.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://status.nimbusmail.example/feed")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://status.nimbusmail.example/")]
    [InlineData("https://status.nimbusmail.example:8443/")]
    [InlineData("http://status.nimbusmail.example:443/")]
    [InlineData("https://status.nimbusmail.example:80/")]
    [InlineData("https://user:secret@status.nimbusmail.example/")]
    [InlineData("http://localhost/")]
    [InlineData("http://intranet/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://2130706433/")] // 127.0.0.1 written as one number
    [InlineData("http://0x7f.1/")] // 127.0.0.1 in hex
    [InlineData("http://017700000001/")] // 127.0.0.1 in octal
    [InlineData("not a url")]
    public void Refuses_anything_that_isnt_a_public_http_page_on_a_standard_port(string input)
    {
        Assert.False(OutboundUrl.TryNormalize(input, out var url, out var error));
        Assert.Null(url);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Refuses_urls_longer_than_the_column()
    {
        var input = "https://status.nimbusmail.example/" + new string('a', OutboundUrl.MaxLength);

        Assert.False(OutboundUrl.TryNormalize(input, out _, out _));
    }
}
