using MassTransit;

using StatusTide.Contracts.CustomPages;

namespace StatusTide.Subscriber.Features.CustomPages;

/// <summary>
/// Answers the Api's <see cref="DetectFeed"/> request. Saves nothing, so a repeat is harmless.
/// </summary>
public class DetectFeedConsumer(FeedDetector detector) : IConsumer<DetectFeed>
{
    public async Task Consume(ConsumeContext<DetectFeed> context)
    {
        switch (await detector.DetectAsync(context.Message.Url, context.CancellationToken))
        {
            case FeedDetected detected:
                await context.RespondAsync(detected);
                break;
            case FeedNotDetected notDetected:
                await context.RespondAsync(notDetected);
                break;
        }
    }
}

/// <summary>
/// A few at a time per replica: each one can wait on a slow site for up to
/// <see cref="FeedDetector.Budget"/>, and polls must not wait behind them.
/// </summary>
public class DetectFeedConsumerDefinition : ConsumerDefinition<DetectFeedConsumer>
{
    public const int ConcurrentDetections = 4;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<DetectFeedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.ConcurrentMessageLimit = ConcurrentDetections;
    }
}
