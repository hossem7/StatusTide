using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StatusTide.Common.Messaging;

public static class MessagingExtensions
{
    /// <summary>
    /// Shared MassTransit setup over RabbitMQ, using the "RabbitMq" connection string
    /// (amqp://user:password@host:5672/). Queue names are kebab-case and every consumer
    /// gets the retry policy in <see cref="MessagingDefaults"/>.
    /// </summary>
    public static IServiceCollection AddStatusTideMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        var connectionString = configuration.GetConnectionString("RabbitMq");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'RabbitMq' is not configured.");
        }

        return services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();
            configureConsumers?.Invoke(bus);

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(connectionString), _ => { });
                rabbit.UseMessageRetry(MessagingDefaults.ConfigureRetry);
                rabbit.ConfigureEndpoints(context);
            });
        });
    }
}
