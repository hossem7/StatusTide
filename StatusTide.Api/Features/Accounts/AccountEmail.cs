using System.Net.Mail;

namespace StatusTide.Api.Features.Accounts;

public static class AccountEmail
{
    public const int MaxLength = 320;

    /// <summary>
    /// The lower-case form used for storing and comparing, or null when it isn't a plain address.
    /// Display names are refused, so "Name &lt;a@b.example&gt;" can't reach the mail headers.
    /// </summary>
    public static string? Normalize(string? email)
    {
        var trimmed = email?.Trim().ToLowerInvariant();
        return trimmed is { Length: > 0 and <= MaxLength }
            && MailAddress.TryCreate(trimmed, out var address)
            && address.Address == trimmed
            && address.Host.Contains('.')
                ? trimmed
                : null;
    }
}
