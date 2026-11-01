namespace StatusTide.Subscriber.Features.Accounts;

public record EmailMessage(string To, string Subject, string TextBody);

/// <summary>Sends one email. SMTP talks to Mailpit locally; production gets Amazon SES in #18.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
