namespace StatusTide.Contracts.Status;

/// <summary>
/// Event published by the Subscriber when a poll finds a real change. Unchanged polls
/// never produce this event. Dashboards, alerts and summaries all react to it.
/// </summary>
public record VendorStatusChanged(Guid VendorId, DateTimeOffset DetectedAt);
