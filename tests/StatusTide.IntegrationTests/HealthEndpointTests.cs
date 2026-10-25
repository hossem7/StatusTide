using System.Net;

namespace StatusTide.IntegrationTests;

public sealed class HealthEndpointTests(PostgresFixture postgres) : IAsyncDisposable
{
    private readonly ApiFactory _factory = new(postgres, "health");

    [Fact]
    public async Task Health_returns_200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
