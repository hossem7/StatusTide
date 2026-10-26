using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz;

namespace StatusTide.Publisher.Features.Polling;

public static class PollingExtensions
{
    /// <summary>
    /// How often the scheduler looks for due vendors. Much shorter than any poll interval,
    /// so a poll starts at most this late.
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    /// <summary>Registers the poll scheduler and the Quartz.NET job that runs it.</summary>
    public static IHostApplicationBuilder AddPollScheduler(this IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SchedulerLock>();
        builder.Services.AddScoped<PollScheduler>();

        builder.AddQuartz(quartz => quartz.ScheduleJob<QueueDuePollsJob>(trigger => trigger
            .WithIdentity("queue-due-polls")
            .WithSimpleSchedule(TickInterval)));
        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return builder;
    }
}
