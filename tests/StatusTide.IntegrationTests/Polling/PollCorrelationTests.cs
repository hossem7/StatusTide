extern alias Publisher;

using System.Collections.Concurrent;

using MassTransit;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Publisher::StatusTide.Publisher.Features.Polling;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using StatusTide.Common.Messaging;
using StatusTide.Contracts.Polling;
using StatusTide.Data.Seed;

namespace StatusTide.IntegrationTests.Polling;

/// <summary>
/// The full path over a real RabbitMQ: the scheduler queues a poll, a consumer receives it,
/// and the consumer's log lines carry the CorrelationId the scheduler chose.
/// </summary>
public sealed class PollCorrelationTests(PostgresFixture postgres, RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task Consumer_logs_carry_the_scheduler_correlation_id()
    {
        var sink = new CapturingSink();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:RabbitMq"] = rabbitMq.ConnectionString })
            .Build();

        await using var services = new ServiceCollection()
            // Keep this logger out of Serilog's static Log.Logger, which tests running in
            // parallel would replace.
            .AddSerilog(logger => logger.Enrich.FromLogContext().WriteTo.Sink(sink), preserveStaticLogger: true)
            .AddLogging()
            .AddSingleton<ReceivedPolls>()
            .AddStatusTideMessaging(configuration, bus => bus.AddConsumer<RecordingPollConsumer>())
            .BuildServiceProvider(true);

        var bus = services.GetRequiredService<IBusControl>();
        await bus.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            // A fresh database holds only the demo vendor, so exactly one poll is queued.
            await using var db = await postgres.CreateMigratedDbContextAsync($"correlation_{Guid.NewGuid():N}");
            var scheduler = new PollScheduler(db, bus, TimeProvider.System, NullLogger<PollScheduler>.Instance);
            await scheduler.QueueDuePollsAsync(TestContext.Current.CancellationToken);

            var received = await services.GetRequiredService<ReceivedPolls>().First.Task
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Equal(AcmeCloudSeed.VendorId, received.VendorId);
            Assert.NotNull(received.CorrelationId);

            var consumerLog = Assert.Single(sink.Events, e => e.MessageTemplate.Text == RecordingPollConsumer.LogMessage);
            Assert.Equal(
                received.CorrelationId.ToString(),
                consumerLog.Properties[CorrelationLogFilter<PollVendor>.PropertyName].ToString());
        }
        finally
        {
            await bus.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    public sealed record ReceivedPoll(Guid VendorId, Guid? CorrelationId);

    public sealed class ReceivedPolls
    {
        public TaskCompletionSource<ReceivedPoll> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class RecordingPollConsumer(ReceivedPolls received, ILogger<RecordingPollConsumer> logger) : IConsumer<PollVendor>
    {
        public const string LogMessage = "Received poll for vendor {VendorId}";

        public Task Consume(ConsumeContext<PollVendor> context)
        {
#pragma warning disable CA2254 // The template is a constant so the test can find this line.
            logger.LogInformation(LogMessage, context.Message.VendorId);
#pragma warning restore CA2254
            received.First.TrySetResult(new ReceivedPoll(context.Message.VendorId, context.CorrelationId));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
