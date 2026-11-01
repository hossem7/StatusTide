using System.Net.Mail;

namespace StatusTide.Subscriber.Features.Accounts;

/// <summary>Plain SMTP with no sign-in, for the local Mailpit inbox.</summary>
public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"] is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException("Email:SmtpHost is not set.");
        var from = configuration["Email:From"] ?? throw new InvalidOperationException("Email:From is not set.");

        using var client = new SmtpClient(host, configuration.GetValue("Email:SmtpPort", 25));
        using var mail = new MailMessage(from, message.To, message.Subject, message.TextBody);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
