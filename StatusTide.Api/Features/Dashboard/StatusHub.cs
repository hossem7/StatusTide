using Microsoft.AspNetCore.SignalR;
using StatusTide.Api.Features.Users;

namespace StatusTide.Api.Features.Dashboard;

/// <summary>
/// Live dashboard updates. Clients only listen: the server sends <see cref="VendorUpdated"/>
/// with a fresh <see cref="VendorCardDto"/> to each follower of a vendor that changed. Messages
/// go to users rather than vendor groups, because each user follows different components and
/// so can see a different status for the same vendor.
/// </summary>
/// <remarks>
/// With one API replica every connection is on this server. Running more replicas needs a
/// SignalR backplane (Redis), or followers connected to another replica miss updates.
/// </remarks>
public class StatusHub : Hub
{
    public const string Path = "/hubs/status";

    /// <summary>Client method that receives one <see cref="VendorCardDto"/>.</summary>
    public const string VendorUpdated = "vendorUpdated";

    /// <summary>Closes connections without a user, since nothing could ever be sent to them.</summary>
    public override Task OnConnectedAsync()
    {
        if (Context.User?.UserId() is null)
        {
            Context.Abort();
        }

        return base.OnConnectedAsync();
    }
}
