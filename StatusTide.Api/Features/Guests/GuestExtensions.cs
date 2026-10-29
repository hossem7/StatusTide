using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StatusTide.Api.Features.Guests;

public static class GuestExtensions
{
    public const string RateLimitPolicy = "guest";

    /// <summary>New guest sessions one address may start per hour.</summary>
    public const int SessionsPerHour = 10;

    /// <summary>
    /// Registers guest sign-in as the default authentication scheme, and a per-address limit on
    /// starting sessions, since every session is a database row. Accounts (#10) add JWT next to it.
    /// </summary>
    public static IServiceCollection AddGuests(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StartGuestCommand>();
        services.AddAuthentication(GuestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, GuestAuthenticationHandler>(GuestAuthenticationHandler.SchemeName, _ => { });
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = SessionsPerHour, Window = TimeSpan.FromHours(1) }));
        });

        return services;
    }
}
