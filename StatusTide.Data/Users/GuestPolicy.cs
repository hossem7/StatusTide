namespace StatusTide.Data.Users;

/// <summary>How long guests last. Shared by the API (cookie) and the Publisher (cleanup job).</summary>
public static class GuestPolicy
{
    /// <summary>A guest inactive this long is deleted, and the cookie expires after the same time.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// How often a visit renews <see cref="User.LastSeenAt"/> and the cookie. Renewing on every
    /// request would write to the database on every page load.
    /// </summary>
    public static readonly TimeSpan RenewAfter = TimeSpan.FromHours(1);
}
