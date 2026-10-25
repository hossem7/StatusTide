using StatusTide.Data.Incidents;
using StatusTide.Data.Users;

namespace StatusTide.Data.Alerts;

public enum AlertChannel
{
    InApp,
    Email,
    Webhook,
}

/// <summary>Each incident sends one alert when it starts and one when it resolves.</summary>
public enum AlertKind
{
    Started,
    Resolved,
}

/// <summary>
/// An alert we sent. The unique index makes a second delivery of the same alert fail,
/// which keeps alert consumers idempotent when a message arrives twice.
/// </summary>
public class AlertDelivery
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid IncidentId { get; set; }

    public AlertChannel Channel { get; set; }
    public AlertKind Kind { get; set; }
    public DateTimeOffset SentAt { get; set; }

    public User? User { get; set; }
    public Incident? Incident { get; set; }
}
