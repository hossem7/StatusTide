using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using StatusTide.Common.Messaging;
using StatusTide.Contracts.Polling;

namespace StatusTide.UnitTests.Messaging;

public class RetryPolicyTests
{
    [Fact]
    public async Task A_consumer_that_fails_once_is_retried_and_succeeds()
    {
        var tracker = new AttemptTracker(failuresBeforeSuccess: 1);
        await using var provider = new ServiceCollection()
            .AddSingleton(tracker)
            .AddMassTransitTestHarness(bus =>
            {
                bus.AddConsumer<FlakyConsumer>();
                bus.UsingInMemory((context, transport) =>
                {
                    transport.UseMessageRetry(MessagingDefaults.ConfigureRetry);
                    transport.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(validateScopes: true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new PollVendor(Guid.NewGuid(), DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        await tracker.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(2, tracker.Attempts);
    }

    sealed class AttemptTracker(int failuresBeforeSuccess)
    {
        int attempts;

        public int Attempts => attempts;

        public TaskCompletionSource Succeeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Attempt()
        {
            if (Interlocked.Increment(ref attempts) <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated transient failure");
            }

            Succeeded.TrySetResult();
        }
    }

    sealed class FlakyConsumer(AttemptTracker tracker) : IConsumer<PollVendor>
    {
        public Task Consume(ConsumeContext<PollVendor> context)
        {
            tracker.Attempt();
            return Task.CompletedTask;
        }
    }
}
