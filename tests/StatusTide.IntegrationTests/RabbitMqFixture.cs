using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(StatusTide.IntegrationTests.RabbitMqFixture))]

namespace StatusTide.IntegrationTests;

/// <summary>One real RabbitMQ container for the whole test run, the same major version as Compose.</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    /// <summary>In the amqp://user:password@host:port/ form that AddStatusTideMessaging expects.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
