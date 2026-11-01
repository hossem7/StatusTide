using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

/// <summary>Confirms the email a link was sent to. A link works once, until it expires.</summary>
public class ConfirmEmailCommand(StatusTideDbContext db, TimeProvider timeProvider)
{
    public async Task<bool> ExecuteAsync(string? token, CancellationToken cancellationToken)
    {
        if (SecureToken.Hash(token) is not { } hash)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var oldestValid = now - AccountPolicy.ConfirmationLifetime;
        return await db.Users
            .Where(u => u.EmailConfirmationHash == hash && u.EmailConfirmationSentAt > oldestValid)
            .ExecuteUpdateAsync(
                u => u.SetProperty(x => x.EmailConfirmedAt, now).SetProperty(x => x.EmailConfirmationHash, (byte[]?)null),
                cancellationToken) == 1;
    }
}
