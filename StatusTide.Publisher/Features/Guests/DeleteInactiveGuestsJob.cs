using Quartz;

namespace StatusTide.Publisher.Features.Guests;

/// <summary>Runs once a night. Safe to run twice: a second run finds nothing to delete.</summary>
[DisallowConcurrentExecution]
public class DeleteInactiveGuestsJob(GuestCleanup cleanup) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        await cleanup.DeleteInactiveGuestsAsync(cancellationToken);
}
