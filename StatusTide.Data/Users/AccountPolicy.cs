namespace StatusTide.Data.Users;

/// <summary>Account time limits. Shared by the API and the Subscriber, which sends the emails.</summary>
public static class AccountPolicy
{
    /// <summary>How long an email confirmation link works.</summary>
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// The shortest gap between two confirmation emails to one account. It stops a resend button
    /// being used to flood an inbox, and makes a redelivered message send nothing.
    /// </summary>
    public static readonly TimeSpan ConfirmationResendAfter = TimeSpan.FromMinutes(1);

    /// <summary>How long a refresh token works if it isn't used. Each use starts a new period.</summary>
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a just-used refresh token is still accepted without counting as reuse. Two tabs
    /// loading together both send the same cookie, and that must not sign the person out.
    /// </summary>
    public static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(10);
}
