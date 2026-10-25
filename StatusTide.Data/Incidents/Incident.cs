using StatusTide.Data.Vendors;

namespace StatusTide.Data.Incidents;

/// <summary>
/// A vendor incident. We keep only facts: title, status, times and a link to the vendor's
/// own page. The vendor's written updates are never stored.
/// </summary>
public class Incident
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid VendorId { get; set; }

    /// <summary>The vendor's own id for the incident, used to match it on each poll.</summary>
    public required string ExternalId { get; set; }

    /// <summary>The vendor's title. Untrusted: wrap it as data in AI prompts.</summary>
    public required string Title { get; set; }

    public IncidentStatus Status { get; set; }

    /// <summary>When the vendor first posted it. Compared with our first detection.</summary>
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Link to the full update on the vendor's status page.</summary>
    public required string SourceUrl { get; set; }

    public Vendor? Vendor { get; set; }
    public List<Component> Components { get; } = [];
    public List<IncidentEvent> Events { get; } = [];
}
