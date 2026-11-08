using Quartz;

using StatusTide.Publisher.Features.Polling;

namespace StatusTide.Publisher.Features.Checks;

/// <summary>
/// Runs once a minute and queues every vendor's check. Shares the scheduler lock with polling,
/// so a second Publisher (during a rollout) does not check every vendor twice.
/// </summary>
[DisallowConcurrentExecution]
public class QueueChecksJob(SchedulerLock schedulerLock, CheckScheduler scheduler, ILogger<QueueChecksJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!await schedulerLock.TryAcquireAsync())
        {
            logger.LogDebug("Another Publisher holds {LockKey}; skipping these checks", SchedulerLock.Key);
            return;
        }

        await scheduler.QueueChecksAsync(cancellationToken);
    }
}
