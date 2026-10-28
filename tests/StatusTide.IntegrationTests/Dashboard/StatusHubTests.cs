using System.Threading.Channels;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Contracts.Status;
using StatusTide.Data.Vendors;

namespace StatusTide.IntegrationTests.Dashboard;

/// <summary>
/// Live updates end to end inside the API: a <see cref="VendorStatusChanged"/> on the bus
/// reaches real SignalR clients connected to <see cref="StatusHub"/>.
/// </summary>
public sealed class StatusHubTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly ApiFactory _factory;
    private readonly DashboardSeed _seed;

    public StatusHubTests(PostgresFixture postgres, RedisFixture redis)
    {
        _factory = new ApiFactory(postgres, "status_hub", services => services
            .AddSingleton(redis.Connection)
            .AddTestAuth());
        _seed = new DashboardSeed(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_follower_receives_the_vendors_new_card()
    {
        var user = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync("Nimbus", ComponentStatus.Operational, ComponentStatus.Operational);
        await _seed.FollowAsync(user, vendor.Components[0]);
        await using var client = await ConnectAsync(user);

        await _seed.SetStatusAsync(vendor.Components[0], ComponentStatus.MajorOutage);
        await PublishChangedAsync(vendor.Id);

        var card = await client.Cards.ReadAsync(Cancel).AsTask().WaitAsync(Timeout, Cancel);
        Assert.Equal(vendor.Id, card.Id);
        Assert.Equal(ComponentStatus.MajorOutage, card.Status);
        Assert.Equal([vendor.Components[0].Id], card.FollowedComponents.Select(c => c.Id));
        Assert.Equal(2, card.ComponentCount);
    }

    [Fact]
    public async Task Each_follower_gets_the_status_of_the_components_they_follow()
    {
        var followsApi = await _seed.AddGuestAsync();
        var followsDocs = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync("Stratus", ComponentStatus.MajorOutage, ComponentStatus.Operational);
        await _seed.FollowAsync(followsApi, vendor.Components[0]);
        await _seed.FollowAsync(followsDocs, vendor.Components[1]);
        await using var apiClient = await ConnectAsync(followsApi);
        await using var docsClient = await ConnectAsync(followsDocs);

        await PublishChangedAsync(vendor.Id);

        Assert.Equal(ComponentStatus.MajorOutage, (await apiClient.Cards.ReadAsync(Cancel).AsTask().WaitAsync(Timeout, Cancel)).Status);
        Assert.Equal(ComponentStatus.Operational, (await docsClient.Cards.ReadAsync(Cancel).AsTask().WaitAsync(Timeout, Cancel)).Status);
    }

    [Fact]
    public async Task A_user_who_does_not_follow_the_vendor_receives_nothing()
    {
        var follower = await _seed.AddGuestAsync();
        var bystander = await _seed.AddGuestAsync();
        var vendor = await _seed.AddVendorAsync("Cirrus", ComponentStatus.Operational);
        var other = await _seed.AddVendorAsync("Cumulus", ComponentStatus.Operational);
        await _seed.FollowAsync(follower, vendor.Components[0]);
        await _seed.FollowAsync(bystander, other.Components[0]);
        await using var followerClient = await ConnectAsync(follower);
        await using var bystanderClient = await ConnectAsync(bystander);

        await PublishChangedAsync(vendor.Id);

        // Both clients use the same transport, so once the follower has its card, a card sent to
        // the bystander would have arrived too.
        await followerClient.Cards.ReadAsync(Cancel).AsTask().WaitAsync(Timeout, Cancel);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Cancel);
        Assert.False(bystanderClient.Cards.TryRead(out _));
    }

    [Fact]
    public async Task A_connection_without_a_user_is_closed()
    {
        await using var connection = BuildConnection(userId: null);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync(Cancel);
        }
        catch (Exception)
        {
            // Closing during the handshake fails StartAsync instead of raising Closed.
            return;
        }

        await closed.Task.WaitAsync(Timeout, Cancel);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private Task PublishChangedAsync(Guid vendorId) =>
        _factory.Services.GetTestHarness().Bus.Publish(new VendorStatusChanged(vendorId, DashboardSeed.Now), Cancel);

    private async Task<HubClient> ConnectAsync(Guid userId)
    {
        var connection = BuildConnection(userId);
        var cards = Channel.CreateUnbounded<VendorCardDto>();
        connection.On<VendorCardDto>(StatusHub.VendorUpdated, card => cards.Writer.TryWrite(card));
        await connection.StartAsync(Cancel);
        return new HubClient(connection, cards.Reader);
    }

    private HubConnection BuildConnection(Guid? userId) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, StatusHub.Path.TrimStart('/')), options =>
        {
            // The in-memory test server has no sockets, so the client talks to it over long polling.
            options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            if (userId is not null)
            {
                options.Headers[TestAuth.Header] = userId.Value.ToString();
            }
        })
        .AddJsonProtocol(json => DashboardExtensions.ConfigureJson(json.PayloadSerializerOptions))
        .Build();

    private sealed record HubClient(HubConnection Connection, ChannelReader<VendorCardDto> Cards) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
