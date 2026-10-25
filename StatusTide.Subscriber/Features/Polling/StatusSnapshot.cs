using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// What a reader found in one feed, in our own model. It only has room for facts:
/// statuses, names, titles, times and links. A vendor's written updates have no field here,
/// so they cannot leak further down the pipeline.
/// </summary>
public record StatusSnapshot(
    IReadOnlyList<ComponentSnapshot> Components,
    IReadOnlyList<IncidentSnapshot> Incidents);

public record ComponentSnapshot(string ExternalId, string Name, ComponentStatus Status);

public record IncidentSnapshot(
    string ExternalId,
    string Title,
    IncidentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    string SourceUrl,
    IReadOnlyList<string> ComponentExternalIds,
    IReadOnlyList<IncidentUpdateSnapshot> Updates);

/// <summary>One vendor update: its status and time, never its text.</summary>
public record IncidentUpdateSnapshot(IncidentStatus Status, DateTimeOffset OccurredAt);
