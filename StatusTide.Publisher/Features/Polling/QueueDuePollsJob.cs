using Quartz;

namespace StatusTide.Publisher.Features.Polling;

/// <summary>Runs every <see cref="PollingExtensions.TickInterval"/> and queues the polls that are due.</summary>
[DisallowConcurrentExecution]
public class QueueDuePollsJob(SchedulerLock schedulerLock, PollScheduler scheduler, ILogger<QueueDuePollsJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!await schedulerLock.TryAcquireAsync())
        {
            logger.LogDebug("Another Publisher holds {LockKey}; skipping this tick", SchedulerLock.Key);
            return;
        }

        await scheduler.QueueDuePollsAsync(cancellationToken);
    }
}
