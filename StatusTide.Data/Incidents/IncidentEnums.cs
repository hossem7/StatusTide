namespace StatusTide.Data.Incidents;

/// <summary>Incident status, using Statuspage's levels.</summary>
public enum IncidentStatus
{
    Investigating,
    Identified,
    Monitoring,
    Resolved,
}

/// <summary>Where an incident event came from: the vendor's feed or our own checks.</summary>
public enum IncidentEventKind
{
    /// <summary>The vendor posted an update. We keep its status and time, never its text.</summary>
    VendorUpdate,

    /// <summary>Our checks failed 3 times in a row.</summary>
    CheckFailing,

    /// <summary>Our checks passed again.</summary>
    CheckRecovered,
}
