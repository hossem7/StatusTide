using MassTransit;

using Microsoft.EntityFrameworkCore;

using StatusTide.Contracts.Accounts;
using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Subscriber.Features.Accounts;

/// <summary>
/// Emails a confirmation link. The token is saved only after the email goes out, so a failed send
/// retries cleanly. A redelivered message then finds the recent send time and does nothing.
/// </summary>
public class SendEmailConfirmationConsumer(
    StatusTideDbContext db,
    IEmailSender email,
    IConfiguration configuration,
    TimeProvider timeProvider) : IConsumer<SendEmailConfirmation>
{
    public async Task Consume(ConsumeContext<SendEmailConfirmation> context)
    {
        var cancellationToken = context.CancellationToken;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == context.Message.UserId, cancellationToken);
        if (user?.Email is null || user.EmailConfirmedAt is not null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (now - user.EmailConfirmationSentAt < AccountPolicy.ConfirmationResendAfter)
        {
            return;
        }

        var (token, hash) = SecureToken.Create();
        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("App:BaseUrl is not set.");
        await email.SendAsync(
            new EmailMessage(
                user.Email,
                "Confirm your StatusTide email",
                $"""
                Confirm your email to turn on StatusTide alerts:

                {baseUrl}/confirm-email?token={token}

                This link works for {AccountPolicy.ConfirmationLifetime.TotalHours:0} hours. If you didn't create an account, you can ignore this email.
                """),
            cancellationToken);

        user.EmailConfirmationHash = hash;
        user.EmailConfirmationSentAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }
}
