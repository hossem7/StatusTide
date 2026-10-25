namespace StatusTide.Data.Incidents;

/// <summary>One point on an incident's timeline: a vendor status change or one of our detections.</summary>
public class IncidentEvent
{
    public long Id { get; init; }
    public Guid IncidentId { get; set; }

    public IncidentEventKind Kind { get; set; }

    /// <summary>The vendor's status at this point. Only set for vendor updates.</summary>
    public IncidentStatus? VendorStatus { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Incident? Incident { get; set; }
}
