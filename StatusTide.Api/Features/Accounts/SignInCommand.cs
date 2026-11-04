using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

/// <summary>The account on success, or how long the email is locked for. Both null for a wrong email or password.</summary>
public record SignInResult(User? User, TimeSpan? LockedFor = null);

/// <summary>Checks an email and password, unless the email has had too many attempts.</summary>
public class SignInCommand(StatusTideDbContext db, IPasswordHasher<User> hasher, SignInLockout lockout, TimeProvider timeProvider)
{
    // Hashed once, so an unknown email costs the same time as a wrong password and
    // response times don't reveal which addresses have accounts.
    private static readonly string UnknownUserHash = new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString());

    public async Task<SignInResult> ExecuteAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (await lockout.CountAttemptAsync(email) is { } lockedFor)
        {
            return new SignInResult(null, lockedFor);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user?.PasswordHash is null)
        {
            hasher.VerifyHashedPassword(new User(), UnknownUserHash, password);
            return new SignInResult(null);
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return new SignInResult(null);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
        }

        user.LastSeenAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await lockout.ClearAsync(email);
        return new SignInResult(user);
    }
}
