using Microsoft.EntityFrameworkCore;

using StatusTide.Data;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

/// <param name="NewToken">Null when the browser already holds the newest token (see <see cref="AccountPolicy.RefreshReuseGrace"/>).</param>
public record RefreshResult(Guid UserId, IssuedRefreshToken? NewToken);

public record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Starts, rotates and ends the refresh tokens of account sign-ins.</summary>
public class RefreshSessions(StatusTideDbContext db, TimeProvider timeProvider, ILogger<RefreshSessions> logger)
{
    public async Task<IssuedRefreshToken> StartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var issued = Add(userId, Guid.CreateVersion7());
        await db.SaveChangesAsync(cancellationToken);
        return issued;
    }

    /// <summary>Swaps a token for the next one, or returns null when it can't be used.</summary>
    public async Task<RefreshResult?> RotateAsync(string? token, CancellationToken cancellationToken)
    {
        if (SecureToken.Hash(token) is not { } hash)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var current = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (current is null || current.RevokedAt is not null || current.ExpiresAt <= now)
        {
            return null;
        }

        // The condition makes the claim atomic: of two requests racing, only one replaces the token.
        var claimed = current.ReplacedAt is null && await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.ReplacedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.ReplacedAt, now), cancellationToken) == 1;
        if (claimed)
        {
            await db.RefreshTokens
                .Where(t => t.UserId == current.UserId && t.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
            var issued = Add(current.UserId, current.FamilyId);
            await db.SaveChangesAsync(cancellationToken);
            return new RefreshResult(current.UserId, issued);
        }

        if (current.ReplacedAt is null || now - current.ReplacedAt <= AccountPolicy.RefreshReuseGrace)
        {
            return new RefreshResult(current.UserId, null);
        }

        logger.LogWarning("A used refresh token came back for user {UserId}, so its sign-in was revoked", current.UserId);
        await RevokeFamilyAsync(current.FamilyId, cancellationToken);
        return null;
    }

    /// <summary>Signs out the sign-in this token belongs to. An unknown token does nothing.</summary>
    public async Task EndAsync(string? token, CancellationToken cancellationToken)
    {
        if (SecureToken.Hash(token) is not { } hash)
        {
            return;
        }

        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (familyId is { } family)
        {
            await RevokeFamilyAsync(family, cancellationToken);
        }
    }

    private Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now), cancellationToken);
    }

    private IssuedRefreshToken Add(Guid userId, Guid familyId)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + AccountPolicy.RefreshLifetime;
        var (token, hash) = SecureToken.Create();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        return new IssuedRefreshToken(token, expiresAt);
    }
}
