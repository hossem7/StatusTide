using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz;

namespace StatusTide.Publisher.Features.Guests;

public static class GuestCleanupExtensions
{
    /// <summary>03:00 UTC every day, when the fewest people are likely to be using the demo.</summary>
    public const string Schedule = "0 0 3 * * ?";

    /// <summary>Registers the nightly job that deletes inactive guests.</summary>
    public static IHostApplicationBuilder AddGuestCleanup(this IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<GuestCleanup>();

        builder.AddQuartz(quartz => quartz.ScheduleJob<DeleteInactiveGuestsJob>(trigger => trigger
            .WithIdentity("delete-inactive-guests")
            .WithCronSchedule(Schedule, cron => cron.InTimeZone(TimeZoneInfo.Utc))));

        return builder;
    }
}
