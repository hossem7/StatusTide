using StatusTide.Data.Follows;

namespace StatusTide.Data.Users;

/// <summary>
/// An account or a guest. Guests are real rows so the demo exercises the real backend.
/// Signing up turns the guest row into an account instead of copying its data.
/// </summary>
public class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Lower-case email. Null for guests.</summary>
    public string? Email { get; set; }

    public bool IsGuest { get; set; }

    /// <summary>
    /// SHA-256 of the guest's cookie token. The token itself is never stored, so a database
    /// leak can't be used to sign in as a guest. Null for accounts.
    /// </summary>
    public byte[]? GuestTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Renewed on each visit. Guests inactive for 30 days are deleted.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public List<Follow> Follows { get; } = [];
}
