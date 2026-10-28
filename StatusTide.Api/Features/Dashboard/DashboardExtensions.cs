using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatusTide.Api.Features.Dashboard;

public static class DashboardExtensions
{
    /// <summary>
    /// Registers the dashboard query and SignalR. Hub messages use the same JSON as the
    /// controllers (see <see cref="ConfigureJson"/>), so a pushed card and a loaded card look
    /// the same to the client.
    /// </summary>
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.AddScoped<DashboardQuery>();
        services.AddSignalR().AddJsonProtocol(json => ConfigureJson(json.PayloadSerializerOptions));
        return services;
    }

    /// <summary>Enums as camelCase strings ("majorOutage"), so the wire format survives reordering an enum.</summary>
    public static void ConfigureJson(JsonSerializerOptions options) =>
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
}
