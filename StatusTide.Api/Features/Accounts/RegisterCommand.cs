using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

public enum RegisterOutcome
{
    Created,
    UpgradedGuest,
    EmailTaken,
    AlreadySignedIn,
}

/// <summary>
/// Creates an account. A guest keeps their row, so their follows carry over with no copying, and
/// removing the guest token ends the guest cookie at once.
/// </summary>
public class RegisterCommand(StatusTideDbContext db, IPasswordHasher<User> hasher, TimeProvider timeProvider)
{
    public async Task<(RegisterOutcome Outcome, User? User)> ExecuteAsync(
        string email,
        string password,
        Guid? callerId,
        CancellationToken cancellationToken)
    {
        var caller = callerId is { } id
            ? await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken)
            : null;
        if (caller is { IsGuest: false })
        {
            return (RegisterOutcome.AlreadySignedIn, null);
        }

        var now = timeProvider.GetUtcNow();
        var user = caller ?? db.Users.Add(new User { CreatedAt = now }).Entity;
        var outcome = caller is null ? RegisterOutcome.Created : RegisterOutcome.UpgradedGuest;
        user.Email = email;
        user.IsGuest = false;
        user.GuestTokenHash = null;
        user.PasswordHash = hasher.HashPassword(user, password);
        user.LastSeenAt = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return (RegisterOutcome.EmailTaken, null);
        }

        return (outcome, user);
    }
}
