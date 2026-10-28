using MassTransit;
using Microsoft.AspNetCore.SignalR;
using StatusTide.Contracts.Status;

namespace StatusTide.Api.Features.Dashboard;

/// <summary>
/// Pushes a vendor's new card to every user who follows one of its components. The card is
/// read from the database when the message arrives, so a duplicate or late message just sends
/// the current state again, which makes this safe to retry.
/// </summary>
public class DashboardPushConsumer(
    DashboardQuery query,
    IHubContext<StatusHub> hub,
    ILogger<DashboardPushConsumer> logger) : IConsumer<VendorStatusChanged>
{
    public async Task Consume(ConsumeContext<VendorStatusChanged> context)
    {
        var vendorId = context.Message.VendorId;
        var cards = await query.GetCardsForFollowersAsync(vendorId, context.CancellationToken);

        // A user with no open connection is skipped by SignalR; they get the snapshot on next load.
        await Task.WhenAll(cards.Select(entry => hub.Clients
            .User(entry.Key.ToString())
            .SendAsync(StatusHub.VendorUpdated, entry.Value, context.CancellationToken)));

        logger.LogInformation("Pushed vendor {VendorId} to {Followers} followers", vendorId, cards.Count);
    }
}
