using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

/// <summary>Checks an email and password. Returns the account, or null for any kind of failure.</summary>
public class SignInCommand(StatusTideDbContext db, IPasswordHasher<User> hasher, TimeProvider timeProvider)
{
    // Hashed once, so an unknown email costs the same time as a wrong password and
    // response times don't reveal which addresses have accounts.
    private static readonly string UnknownUserHash = new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString());

    public async Task<User?> ExecuteAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(new User(), UnknownUserHash, password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
        }

        user.LastSeenAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
