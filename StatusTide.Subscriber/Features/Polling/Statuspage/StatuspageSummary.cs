namespace StatusTide.Subscriber.Features.Polling.Statuspage;

// The parts of Statuspage's /api/v2/summary.json that we use. Field names map through the
// snake_case naming policy. Update "body" text is deliberately left out, so it is never
// even read into memory.

internal record StatuspageSummary(
    List<StatuspageComponent>? Components,
    List<StatuspageIncident>? Incidents);

internal record StatuspageComponent(
    string? Id,
    string? Name,
    string? Status,
    bool Group);

internal record StatuspageIncident(
    string? Id,
    string? Name,
    string? Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ResolvedAt,
    string? Shortlink,
    List<StatuspageIncidentComponent>? Components,
    List<StatuspageIncidentUpdate>? IncidentUpdates);

internal record StatuspageIncidentComponent(string? Id);

internal record StatuspageIncidentUpdate(
    string? Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? DisplayAt,
    List<StatuspageAffectedComponent>? AffectedComponents);

internal record StatuspageAffectedComponent(string? Code);
