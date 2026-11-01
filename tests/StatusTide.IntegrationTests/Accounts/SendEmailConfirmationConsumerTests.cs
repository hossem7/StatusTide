extern alias Subscriber;

using System.Text.RegularExpressions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using StatusTide.Contracts.Accounts;
using StatusTide.Data;
using StatusTide.Data.Users;

using Subscriber::StatusTide.Subscriber.Features.Accounts;

namespace StatusTide.IntegrationTests.Accounts;

/// <summary>The confirmation email worker against real PostgreSQL, with a fake mail server.</summary>
public sealed partial class SendEmailConfirmationConsumerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeEmailSender _email = new();
    private readonly string _database = $"account_email_{Guid.NewGuid():N}";
    private ServiceProvider _services = null!;
    private ITestHarness _harness = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await (await postgres.CreateMigratedDbContextAsync(_database)).DisposeAsync();

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://app.example/" })
                .Build())
            .AddSingleton<IEmailSender>(_email)
            .AddDbContext<StatusTideDbContext>(options => options
                .UseNpgsql(postgres.ConnectionString(_database))
                .UseSnakeCaseNamingConvention())
            .AddMassTransitTestHarness(bus => bus.AddConsumer<SendEmailConfirmationConsumer>())
            .BuildServiceProvider(true);

        _harness = _services.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task Sends_a_link_whose_token_matches_the_stored_hash()
    {
        var userId = await AddAccountAsync();

        await SendAsync(userId);

        var message = Assert.Single(_email.Sent);
        Assert.Equal($"{userId:N}@acme.example", message.To);
        var token = TokenInLink().Match(message.TextBody).Groups[1].Value;
        var user = await LoadAsync(userId);
        Assert.Equal(SecureToken.Hash(token), user.EmailConfirmationHash);
        Assert.Equal(Now, user.EmailConfirmationSentAt);
    }

    [Fact]
    public async Task A_redelivered_message_sends_nothing_more()
    {
        var userId = await AddAccountAsync();

        await SendAsync(userId);
        await SendAsync(userId);

        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task A_resend_after_the_wait_replaces_the_old_link()
    {
        var userId = await AddAccountAsync();
        await SendAsync(userId);
        var firstHash = (await LoadAsync(userId)).EmailConfirmationHash;

        _time.Advance(AccountPolicy.ConfirmationResendAfter);
        await SendAsync(userId);

        Assert.Equal(2, _email.Sent.Count);
        Assert.NotEqual(firstHash, (await LoadAsync(userId)).EmailConfirmationHash);
    }

    [Fact]
    public async Task A_failed_send_saves_no_token_so_the_retry_still_sends()
    {
        var userId = await AddAccountAsync();
        _email.FailNext = true;

        await SendAsync(userId, expectFault: true);

        var user = await LoadAsync(userId);
        Assert.Null(user.EmailConfirmationHash);
        Assert.Null(user.EmailConfirmationSentAt);

        await SendAsync(userId);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task A_confirmed_account_gets_no_email()
    {
        var userId = await AddAccountAsync(confirmed: true);

        await SendAsync(userId);

        Assert.Empty(_email.Sent);
    }

    private async Task SendAsync(Guid userId, bool expectFault = false)
    {
        var id = NewId.NextGuid();
        await _harness.Bus.Publish(new SendEmailConfirmation(userId), context => context.CorrelationId = id, Cancel);

        // Waits for this one message. The harness's own Any() stops waiting once the bus has been
        // idle for a moment, which happens between the messages of one test.
        var consumer = _harness.GetConsumerHarness<SendEmailConfirmationConsumer>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        IReceivedMessage<SendEmailConfirmation>? consumed;
        while ((consumed = consumer.Consumed.Select<SendEmailConfirmation>(m => m.Context.CorrelationId == id, Cancel).FirstOrDefault()) is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        Assert.Equal(expectFault, consumed.Exception is not null);
    }

    private async Task<Guid> AddAccountAsync(bool confirmed = false)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();
        var user = new User { CreatedAt = Now, LastSeenAt = Now, EmailConfirmedAt = confirmed ? Now : null };
        user.Email = $"{user.Id:N}@acme.example";
        db.Users.Add(user);
        await db.SaveChangesAsync(Cancel);
        return user.Id;
    }

    private async Task<User> LoadAsync(Guid userId)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StatusTideDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, Cancel);
    }

    [GeneratedRegex(@"https://app\.example/confirm-email\?token=([A-Za-z0-9_-]{43})")]
    private static partial Regex TokenInLink();

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool FailNext { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("The mail server said no.");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
