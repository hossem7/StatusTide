using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz;

using StatusTide.Data.Checks;
using StatusTide.Publisher.Features.Polling;

namespace StatusTide.Publisher.Features.Checks;

public static class CheckExtensions
{
    /// <summary>03:30 UTC every day, after the guest cleanup at 03:00.</summary>
    public const string RollupSchedule = "0 30 3 * * ?";

    /// <summary>Registers the job that queues our own checks every minute and the nightly rollup.</summary>
    public static IHostApplicationBuilder AddChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<SchedulerLock>();
        builder.Services.AddScoped<CheckScheduler>();
        builder.Services.AddScoped<CheckHousekeeping>();

        builder.AddQuartz(quartz =>
        {
            quartz.ScheduleJob<QueueChecksJob>(trigger => trigger
                .WithIdentity("queue-checks")
                .WithSimpleSchedule(CheckPolicy.Interval));

            quartz.ScheduleJob<RollUpChecksJob>(trigger => trigger
                .WithIdentity("roll-up-checks")
                .WithCronSchedule(RollupSchedule, cron => cron.InTimeZone(TimeZoneInfo.Utc)));
        });

        return builder;
    }
}
