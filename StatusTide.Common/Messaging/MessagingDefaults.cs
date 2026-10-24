using MassTransit;

namespace StatusTide.Common.Messaging;

public static class MessagingDefaults
{
    /// <summary>
    /// Retry policy for every consumer: 5 retries with exponential backoff from 1 to 30
    /// seconds. A message that still fails is moved to the queue's "_error" queue by
    /// MassTransit, where it can be inspected and replayed.
    /// </summary>
    public static void ConfigureRetry(IRetryConfigurator retry) =>
        retry.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(30),
            intervalDelta: TimeSpan.FromSeconds(2));
}
