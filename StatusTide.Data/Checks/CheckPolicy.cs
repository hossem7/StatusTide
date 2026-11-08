namespace StatusTide.Data.Checks;

/// <summary>The rules for our own checks, shared by the jobs that queue them and the worker that runs them.</summary>
public static class CheckPolicy
{
    /// <summary>How often each vendor's check URL is called.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>Failures in a row before we call a vendor down. One failure is often our own network.</summary>
    public const int FailuresToMarkDown = 3;

    /// <summary>How long raw checks are kept. After that only the hourly rollups remain.</summary>
    public static readonly TimeSpan RawRetention = TimeSpan.FromDays(7);
}
