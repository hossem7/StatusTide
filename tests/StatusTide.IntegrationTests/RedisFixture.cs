using StackExchange.Redis;

using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(StatusTide.IntegrationTests.RedisFixture))]

namespace StatusTide.IntegrationTests;

/// <summary>One real Redis container for the whole test run, the same major version as Compose.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:8-alpine").Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
        {
            await Connection.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
