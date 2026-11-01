namespace StatusTide.Data.Users;

/// <summary>
/// One refresh token of an account's sign-in. Every use replaces the token with a new one in the
/// same family, and used tokens stay until they expire so that reuse can be spotted: a used token
/// coming back means it may have been stolen, so the whole family is revoked.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    /// <summary>All tokens from one sign-in share this id. Signing out revokes the family.</summary>
    public Guid FamilyId { get; init; }

    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    public required byte[] TokenHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When this token was used and replaced by the next one.</summary>
    public DateTimeOffset? ReplacedAt { get; set; }

    /// <summary>When the family was revoked by signing out or by reuse.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public User? User { get; init; }
}
