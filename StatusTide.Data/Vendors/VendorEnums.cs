namespace StatusTide.Data.Vendors;

/// <summary>Which reader handles a vendor's feed.</summary>
public enum FeedFormat
{
    Statuspage,
    Rss,
    Slack,
    Google,
    Demo,
}

/// <summary>
/// Component status, using Statuspage's levels since most feeds map onto them.
/// Unknown means we could not read the feed, for example after a 429 or a timeout.
/// </summary>
public enum ComponentStatus
{
    Unknown,
    Operational,
    DegradedPerformance,
    PartialOutage,
    MajorOutage,
    UnderMaintenance,
}

public enum VerificationDecision
{
    Approved,
    Rejected,
}
