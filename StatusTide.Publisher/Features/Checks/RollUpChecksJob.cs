using Quartz;

namespace StatusTide.Publisher.Features.Checks;

/// <summary>Runs once a night. Safe to run twice: rollups are upserts and old checks are already gone.</summary>
[DisallowConcurrentExecution]
public class RollUpChecksJob(CheckHousekeeping housekeeping) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        await housekeeping.RunAsync(cancellationToken);
}
